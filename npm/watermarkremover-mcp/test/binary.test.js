/*
 * @watermarkremover/mcp — Node test suite
 * =======================================
 *
 * Pure unit tests for the resolution + install helpers. No network
 * calls, no filesystem writes outside the test sandbox. Runs under
 * `node --test` (Node 18+) — no test runner dependency.
 *
 * The .NET-side tests in `WatermarkRemover.CLI.Tests` cover the
 * *static* shape of the package (manifest, bin entry, file presence);
 * these tests cover the *dynamic* behaviour (platform detection,
 * release URL shape, zip extraction edge cases).
 */

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const os = require('node:os');

const {
  RID_TABLE,
  detectRuntimeId,
  releaseAssetUrl,
  installedBinaryPath,
  resolveBinary,
} = require('../lib/binary');
const {
  looksLikeBinaryEntry,
  extractWatermarkRemoverBinary,
} = require('../lib/install');

test('RID_TABLE covers every published platform', () => {
  const pairs = RID_TABLE.map((r) => `${r.platform}/${r.arch}`);
  // The release workflow currently publishes 4 RIDs (see
  // .github/workflows/release.yml). Keep the assertion in sync —
  // if the matrix grows, the table must grow too.
  assert.ok(pairs.includes('linux/x64'));
  assert.ok(pairs.includes('linux/arm64'));
  assert.ok(pairs.includes('darwin/x64'));
  assert.ok(pairs.includes('win32/x64'));
  assert.equal(pairs.length, 4, "only the 4 published RIDs are supported by postinstall");
});

test('detectRuntimeId resolves the current host', () => {
  const rid = detectRuntimeId();
  assert.equal(typeof rid.platform, 'string');
  assert.equal(typeof rid.arch, 'string');
  assert.equal(typeof rid.rid, 'string');
  assert.equal(typeof rid.suffix, 'string');
  assert.ok(rid.suffix.length > 0);
});

test('detectRuntimeId throws on unsupported platforms', () => {
  assert.throws(
    () => detectRuntimeId({ platform: 'freebsd', arch: 'x64' }),
    /Unsupported platform freebsd\/x64/,
  );
  assert.throws(
    () => detectRuntimeId({ platform: 'linux', arch: 'ia32' }),
    /Unsupported platform linux\/ia32/,
  );
});

test('releaseAssetUrl normalises the v-prefix and uses the right repo', () => {
  const url = releaseAssetUrl('1.2.3', 'linux-x64');
  assert.equal(
    url,
    'https://github.com/techbuzzz/ai-watermark-remover/releases/download/v1.2.3/watermarkremover-linux-x64.zip',
  );
  // Without leading `v` — the input is the bare semver.
  const urlNoV = releaseAssetUrl('1.2.3-rc.1', 'darwin-arm64');
  assert.ok(urlNoV.includes('/v1.2.3-rc.1/watermarkremover-darwin-arm64.zip'));
  // Allow custom repo override (used by tests + downstream forks).
  const urlFork = releaseAssetUrl('1.2.3', 'win-x64', { repo: 'me/fork' });
  assert.ok(urlFork.startsWith('https://github.com/me/fork/'));
});

test('installedBinaryPath picks the right executable name per platform', () => {
  const dir = path.join(os.tmpdir(), 'wr-mcp-test');
  const linux = installedBinaryPath(dir, { platform: 'linux' });
  const win = installedBinaryPath(dir, { platform: 'win32' });
  assert.equal(linux, path.join(dir, 'bin', 'watermarkremover'));
  assert.equal(win, path.join(dir, 'bin', 'watermarkremover.exe'));
});

test('resolveBinary prefers WATERMARKREMOVER_BINARY when set', () => {
  const fakePath = path.join(os.tmpdir(), 'wr-fake-binary');
  const result = resolveBinary({
    env: { WATERMARKREMOVER_BINARY: fakePath },
    packageDir: path.join(os.tmpdir(), 'wr-no-package'),
    existsSync: (p) => p === fakePath,
  });
  assert.equal(result.source, 'env');
  assert.equal(result.path, fakePath);
});

test('resolveBinary falls back to the postinstall target when present', () => {
  const packageDir = path.join(os.tmpdir(), 'wr-pkg');
  const installed = path.join(packageDir, 'bin', 'watermarkremover');
  const result = resolveBinary({
    env: {},
    packageDir,
    platform: 'linux',
    existsSync: (p) => p === installed,
  });
  assert.equal(result.source, 'installed');
  assert.equal(result.path, installed);
});

test('resolveBinary walks PATH as a last resort', () => {
  const dir = path.join(os.tmpdir(), 'wr-on-path');
  const exe = process.platform === 'win32' ? 'watermarkremover.exe' : 'watermarkremover';
  const result = resolveBinary({
    env: {},
    packageDir: path.join(os.tmpdir(), 'wr-no-package'),
    pathEnv: dir,
    existsSync: (p) => p === path.join(dir, exe),
  });
  assert.equal(result.source, 'path');
  assert.equal(result.path, path.join(dir, exe));
});

test('resolveBinary returns "unresolved" when nothing matches', () => {
  const result = resolveBinary({
    env: {},
    packageDir: path.join(os.tmpdir(), 'wr-no-package'),
    pathEnv: '',
    existsSync: () => false,
  });
  assert.equal(result.source, 'unresolved');
  assert.equal(result.path, 'watermarkremover');
});

test('looksLikeBinaryEntry matches the canonical filenames and nested layouts', () => {
  const rid = { suffix: 'linux-x64' };
  assert.equal(looksLikeBinaryEntry('watermarkremover', rid), true);
  assert.equal(looksLikeBinaryEntry('watermarkremover.exe', rid), true);
  assert.equal(looksLikeBinaryEntry('watermarkremover-linux-x64/watermarkremover', rid), true);
  assert.equal(looksLikeBinaryEntry('README.md', rid), false);
  assert.equal(looksLikeBinaryEntry('watermarkremover.pdb', rid), false);
  // Even when the archive nests the binary under a different RID dir
  // (cross-RID build that landed in the wrong artefact), the flat-name
  // matches so we still pick it up.
  assert.equal(looksLikeBinaryEntry('watermarkremover-win-x64/watermarkremover.exe', rid), false);
});

/**
 * Build a minimal valid ZIP file in memory containing a single
 * STORED entry. Sufficient to exercise the central-directory reader
 * without pulling in a ZIP library.
 */
function buildZip(entries) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;

  for (const entry of entries) {
    const nameBuf = Buffer.from(entry.name, 'utf8');
    const crc = entry.crc >>> 0;
    const method = entry.method ?? 0;
    const data = entry.data;
    const compressed = method === 0 ? data : data; // tests use STORED only

    const lfh = Buffer.alloc(30);
    lfh.writeUInt32LE(0x04034b50, 0); // local file header signature
    lfh.writeUInt16LE(20, 4); // version needed
    lfh.writeUInt16LE(0, 6); // flags
    lfh.writeUInt16LE(method, 8);
    lfh.writeUInt16LE(0, 10); // mod time
    lfh.writeUInt16LE(0, 12); // mod date
    lfh.writeUInt32LE(crc, 14);
    lfh.writeUInt32LE(compressed.length, 18);
    lfh.writeUInt32LE(compressed.length, 22);
    lfh.writeUInt16LE(nameBuf.length, 26);
    lfh.writeUInt16LE(0, 28);
    localParts.push(lfh, nameBuf, compressed);

    const cdh = Buffer.alloc(46);
    cdh.writeUInt32LE(0x02014b50, 0); // central dir header signature
    cdh.writeUInt16LE(20, 4); // version made by
    cdh.writeUInt16LE(20, 6); // version needed
    cdh.writeUInt16LE(0, 8); // flags
    cdh.writeUInt16LE(method, 10);
    cdh.writeUInt16LE(0, 12);
    cdh.writeUInt16LE(0, 14);
    cdh.writeUInt32LE(crc, 16);
    cdh.writeUInt32LE(compressed.length, 20);
    cdh.writeUInt32LE(compressed.length, 24);
    cdh.writeUInt16LE(nameBuf.length, 28);
    cdh.writeUInt16LE(0, 30);
    cdh.writeUInt16LE(0, 32);
    cdh.writeUInt16LE(0, 34);
    cdh.writeUInt16LE(0, 36);
    cdh.writeUInt32LE(0, 38);
    cdh.writeUInt32LE(offset, 42);
    centralParts.push(cdh, nameBuf);

    offset += lfh.length + nameBuf.length + compressed.length;
  }

  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(centralParts.reduce((s, b) => s + b.length, 0), 12);
  eocd.writeUInt32LE(offset, 16);
  eocd.writeUInt16LE(0, 20);

  return Buffer.concat([...localParts, ...centralParts, eocd]);
}

/** CRC-32 matching the polynomial the ZIP spec mandates. */
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

test('extractWatermarkRemoverBinary reads a STORED entry', () => {
  const data = Buffer.from('hello world\n', 'utf8');
  const zip = buildZip([
    {
      name: 'README.md',
      method: 0,
      data: data,
      crc: crc32(data),
    },
    {
      name: 'watermarkremover',
      method: 0,
      data: data,
      crc: crc32(data),
    },
  ]);

  const out = extractWatermarkRemoverBinary(zip, { suffix: 'linux-x64' });
  assert.equal(out.toString('utf8'), 'hello world\n');
});

test('extractWatermarkRemoverBinary throws on a corrupt archive', () => {
  assert.throws(() => extractWatermarkRemoverBinary(Buffer.from('not a zip'), { suffix: 'linux-x64' }));
});

test('extractWatermarkRemoverBinary throws when no binary entry exists', () => {
  const data = Buffer.from('just readme', 'utf8');
  const zip = buildZip([
    { name: 'README.md', method: 0, data, crc: crc32(data) },
  ]);
  assert.throws(
    () => extractWatermarkRemoverBinary(zip, { suffix: 'linux-x64' }),
    /no entry matching 'watermarkremover'/,
  );
});
