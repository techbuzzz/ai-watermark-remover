/*
 * @watermarkremover/mcp — postinstall install helper
 * ===================================================
 *
 * Downloads the platform-appropriate WatermarkRemover release artefact
 * and extracts the `watermarkremover[.exe]` binary into
 * `<packageDir>/bin/`. The download is intentionally tolerant: failures
 * are reported on stderr but never throw an unhandled rejection — the
 * caller (postinstall.js) converts them to a soft "exit 0 + warning"
 * so a network blip during `npm install` does not break the user's
 * install. They can always re-run with `WR_FORCE_BINARY_DOWNLOAD=1` or
 * wire up `WATERMARKREMOVER_BINARY` to recover.
 *
 * The HTTP fetch uses only Node built-ins so this package has **zero
 * runtime dependencies**. No `node-fetch`, no `adm-zip`, no
 * `tar-stream`. The zip extraction is a minimal central-directory
 * reader that handles the DEFLATE-stored entries the release
 * workflow emits (it does not need to be a full PKZip parser — see
 * the `extractZip` notes for the exact subset we support).
 */

'use strict';

const fs = require('node:fs');
const fsp = require('node:fs/promises');
const http = require('node:http');
const https = require('node:https');
const path = require('node:path');
const zlib = require('node:zlib');
const { Buffer } = require('node:buffer');

const { detectRuntimeId, releaseAssetUrl, installedBinaryPath } = require('./binary');

/**
 * Top-level orchestrator. Idempotent — re-running with the same
 * version is a no-op when the binary already exists.
 *
 * @param {{
 *   env: NodeJS.ProcessEnv,
 *   cwd: string,
 *   expectedVersion: string,
 *   log?: (msg: string) => void,
 *   error?: (msg: string) => void,
 *   fetchImpl?: typeof fetch,
 *   existsSync?: (p: string) => boolean,
 *   mkdir?: (p: string, opts?: { recursive?: boolean }) => Promise<void>,
 *   writeFile?: (p: string, data: Buffer) => Promise<void>,
 *   chmod?: (p: string, mode: number) => Promise<void>,
 * }} opts
 * @returns {Promise<{ installed: boolean, reason: string, binaryPath?: string }>}
 */
async function installBinary(opts) {
  const {
    env,
    cwd,
    expectedVersion,
    log = () => {},
    error,
    fetchImpl,
    existsSync = fs.existsSync,
    mkdir = (p) => fsp.mkdir(p, { recursive: true }),
    writeFile = (p, data) => fsp.writeFile(p, data),
    chmod = (p, mode) => fsp.chmod(p, mode),
  } = opts;

  const say = (msg) => {
    if (log) log(msg);
  };
  const fail = (msg) => {
    if (error) error(msg);
  };

  // Skip conditions -------------------------------------------------
  if (env.WR_SKIP_BINARY_DOWNLOAD === '1') {
    return { installed: false, reason: 'WR_SKIP_BINARY_DOWNLOAD=1' };
  }

  const rid = detectRuntimeId();
  const target = installedBinaryPath(cwd, { platform: process.platform });

  if (existsSync(target) && !env.WR_FORCE_BINARY_DOWNLOAD) {
    return { installed: true, reason: 'already present', binaryPath: target };
  }

  // Download --------------------------------------------------------
  const url = releaseAssetUrl(expectedVersion, rid.suffix);
  say(`downloading ${url}`);

  let zipBytes;
  try {
    zipBytes = await downloadWithRedirects(url, fetchImpl);
  } catch (err) {
    fail(`could not download ${url}: ${err.message}`);
    return { installed: false, reason: 'download failed' };
  }

  // Extract ---------------------------------------------------------
  let binaryBuffer;
  try {
    binaryBuffer = extractWatermarkRemoverBinary(zipBytes, rid);
  } catch (err) {
    fail(`could not extract binary: ${err.message}`);
    return { installed: false, reason: 'extraction failed' };
  }

  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, binaryBuffer);
  if (process.platform !== 'win32') {
    await chmod(target, 0o755);
  }

  say(`installed ${target} (${binaryBuffer.length} bytes)`);
  return { installed: true, reason: 'downloaded', binaryPath: target };
}

/**
 * `fetch` shim that uses Node's built-in `http`/`https` so the
 * package stays dependency-free. Returns a `Buffer` with the response
 * body. Follows up to 5 redirects — GitHub Releases uses a 302 to
 * `objects.githubusercontent.com`.
 */
function downloadWithRedirects(url, fetchImpl) {
  if (typeof fetchImpl === 'function') {
    return (async () => {
      const res = await fetchImpl(url, { redirect: 'follow' });
      if (!res.ok) {
        throw new Error(`HTTP ${res.status} ${res.statusText}`);
      }
      const ab = await res.arrayBuffer();
      return Buffer.from(ab);
    })();
  }

  return new Promise((resolve, reject) => {
    const seen = new Set();
    const visit = (current) => {
      if (seen.has(current)) {
        reject(new Error(`redirect loop at ${current}`));
        return;
      }
      if (seen.size > 5) {
        reject(new Error('too many redirects'));
        return;
      }
      seen.add(current);

      let mod;
      try {
        const u = new URL(current);
        mod = u.protocol === 'https:' ? https : http;
      } catch (err) {
        reject(err);
        return;
      }

      mod
        .get(current, (res) => {
          const { statusCode, headers } = res;
          if (
            statusCode &&
            statusCode >= 300 &&
            statusCode < 400 &&
            headers &&
            headers.location
          ) {
            res.resume();
            const next = new URL(headers.location, current).toString();
            visit(next);
            return;
          }
          if (!statusCode || statusCode >= 400) {
            res.resume();
            reject(new Error(`HTTP ${statusCode} ${res.statusMessage || ''}`.trim()));
            return;
          }
          const chunks = [];
          res.on('data', (c) => chunks.push(c));
          res.on('end', () => resolve(Buffer.concat(chunks)));
          res.on('error', reject);
        })
        .on('error', reject);
    };

    visit(url);
  });
}

/**
 * Minimal ZIP central-directory reader. Looks for the entry whose
 * filename starts with `watermarkremover` and is **not** a directory
 * marker. Supports STORED (method 0) and DEFLATE (method 8) entries —
 * the release workflow emits uncompressed artefacts, but
 * `EnableCompressionInSingleFile=true` sometimes flips a few entries
 * to DEFLATE so we handle both.
 */
function extractWatermarkRemoverBinary(zipBytes, rid) {
  // End-of-central-directory record signature: 0x06054b50.
  const EOCD_SIG = 0x06054b50;
  // Central directory file header signature: 0x02014b50.
  const CDH_SIG = 0x02014b50;
  // Local file header signature: 0x04034b50.
  const LFH_SIG = 0x04034b50;

  const buf = Buffer.from(zipBytes);
  const len = buf.length;

  // Find the EOCD record by scanning the last 64 KiB — the spec
  // allows the comment to be up to 65535 bytes.
  let eocdOffset = -1;
  for (let i = len - 22; i >= Math.max(0, len - 0x10000); i--) {
    if (buf.readUInt32LE(i) === EOCD_SIG) {
      eocdOffset = i;
      break;
    }
  }
  if (eocdOffset < 0) {
    throw new Error('end-of-central-directory record not found (not a valid zip)');
  }

  const cdEntries = buf.readUInt16LE(eocdOffset + 10);
  const cdSize = buf.readUInt32LE(eocdOffset + 12);
  const cdOffset = buf.readUInt32LE(eocdOffset + 16);
  if (cdEntries === 0xffff || cdSize === 0xffffffff) {
    throw new Error('zip64 archives are not supported by the in-tree extractor');
  }

  // Walk the central directory looking for `watermarkremover[.exe]`.
  let cursor = cdOffset;
  let found = null;
  for (let i = 0; i < cdEntries; i++) {
    if (cursor + 46 > len || buf.readUInt32LE(cursor) !== CDH_SIG) {
      throw new Error('central directory entry header missing or corrupt');
    }
    const nameLen = buf.readUInt16LE(cursor + 28);
    const extraLen = buf.readUInt16LE(cursor + 30);
    const commentLen = buf.readUInt16LE(cursor + 32);
    const localHeaderOffset = buf.readUInt32LE(cursor + 42);
    const name = buf.slice(cursor + 46, cursor + 46 + nameLen).toString('utf8');
    if (looksLikeBinaryEntry(name, rid)) {
      found = { name, localHeaderOffset };
      break;
    }
    cursor += 46 + nameLen + extraLen + commentLen;
  }

  if (!found) {
    throw new Error(
      `no entry matching 'watermarkremover' (any platform) found in archive ` +
        `(${cdEntries} entries total)`,
    );
  }

  // Read the local file header + payload.
  const lh = found.localHeaderOffset;
  if (buf.readUInt32LE(lh) !== LFH_SIG) {
    throw new Error('local file header signature mismatch');
  }
  const method = buf.readUInt16LE(lh + 8);
  const compSize = buf.readUInt32LE(lh + 18);
  const fnameLen = buf.readUInt16LE(lh + 26);
  const extraLen2 = buf.readUInt16LE(lh + 28);
  const dataStart = lh + 30 + fnameLen + extraLen2;
  const dataEnd = dataStart + compSize;
  if (dataEnd > len) {
    throw new Error('local file header points past the end of the archive');
  }
  const compressed = buf.subarray(dataStart, dataEnd);
  if (method === 0) {
    return Buffer.from(compressed);
  }
  if (method === 8) {
    try {
      return Buffer.from(zlib.inflateRawSync(compressed));
    } catch (err) {
      throw new Error(`DEFLATE inflation failed: ${err.message}`);
    }
  }
  throw new Error(`unsupported zip compression method ${method}`);
}

function looksLikeBinaryEntry(name, rid) {
  const lastSlash = name.lastIndexOf('/');
  const base = lastSlash >= 0 ? name.substring(lastSlash + 1) : name;
  if (base !== 'watermarkremover' && base !== 'watermarkremover.exe') {
    return false;
  }
  if (lastSlash < 0) {
    // Flat entry — the package name itself is the binary. This is
    // what the release workflow produces today.
    return true;
  }
  // Nested entry — the directory prefix must match the current RID
  // so we never pick up a cross-RID artefact that landed in the
  // wrong archive (defensive against human error).
  const prefix = name.substring(0, lastSlash);
  return prefix === `watermarkremover-${rid.suffix}`;
}

module.exports = {
  installBinary,
  downloadWithRedirects,
  extractWatermarkRemoverBinary,
  looksLikeBinaryEntry,
};
