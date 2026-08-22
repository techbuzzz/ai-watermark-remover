/*
 * @watermarkremover/mcp — binary resolution
 * =========================================
 *
 * Pure (no network) helpers that decide *which* binary to spawn and
 * what GitHub Release asset name to look for. Kept side-effect-free so
 * the unit tests can exercise every platform branch without touching
 * the filesystem.
 *
 * The runtime identifier matrix mirrors the one in
 * `.github/workflows/release.yml`. If the release workflow gains a
 * new RID, add it here too — the postinstall step will silently skip
 * unsupported platforms otherwise.
 */

'use strict';

const path = require('node:path');
const fs = require('node:fs');

/**
 * Runtime identifier (RID) → GitHub Release asset name fragment.
 *
 * Each entry maps a Node `process.platform` + `process.arch` pair to
 * the corresponding `.NET` runtime identifier and the suffix used in
 * the release artefact name (`watermarkremover-<suffix>.zip`).
 *
 * Kept in lockstep with the matrix in
 * `.github/workflows/release.yml` — adding a new RID there means
 * adding it here too, otherwise the postinstall step silently fails
 * with a 404 on unsupported platforms.
 */
const RID_TABLE = Object.freeze([
  { platform: 'linux', arch: 'x64', rid: 'linux-x64', suffix: 'linux-x64' },
  { platform: 'linux', arch: 'arm64', rid: 'linux-arm64', suffix: 'linux-arm64' },
  { platform: 'darwin', arch: 'x64', rid: 'osx-x64', suffix: 'osx-x64' },
  { platform: 'win32', arch: 'x64', rid: 'win-x64', suffix: 'win-x64' },
]);

/**
 * Resolve the current platform's runtime identifier. Throws on
 * unsupported platforms (e.g. `freebsd`, `openbsd`, `linux32`).
 *
 * @param {{ platform?: string, arch?: string }} [env]
 *   Override for testing — defaults to `process.platform` /
 *   `process.arch`.
 * @returns {{ platform: string, arch: string, rid: string, suffix: string }}
 */
function detectRuntimeId(env) {
  const platform = (env && env.platform) || process.platform;
  const arch = (env && env.arch) || process.arch;
  const match = RID_TABLE.find((row) => row.platform === platform && row.arch === arch);
  if (!match) {
    throw new Error(
      `Unsupported platform ${platform}/${arch}. ` +
        `Supported: ${RID_TABLE.map((r) => `${r.platform}/${r.arch}`).join(', ')}.`,
    );
  }
  return match;
}

/**
 * Build the GitHub Releases download URL for the given tag + RID.
 * Exposed so the install code and the tests share the same shape.
 *
 * @param {string} tag
 *   Release tag, with or without the leading `v` (we add it).
 * @param {string} suffix
 *   The asset-name suffix (one of the `RID_TABLE` rows).
 * @param {{ repo?: string }} [opts]
 *   Override the `owner/repo` for testing. Defaults to
 *   `techbuzzz/ai-watermark-remover`.
 * @returns {string}
 */
function releaseAssetUrl(tag, suffix, opts) {
  const repo = (opts && opts.repo) || 'techbuzzz/ai-watermark-remover';
  const cleanTag = String(tag || '').replace(/^v/, '');
  return `https://github.com/${repo}/releases/download/v${cleanTag}/watermarkremover-${suffix}.zip`;
}

/**
 * Absolute path of the extracted binary for a given package directory
 * + RID. The postinstall step drops the unzipped artefact here.
 *
 * @param {string} packageDir
 *   Absolute path to the directory that contains `package.json`.
 * @param {{ platform?: string }} [opts]
 *   Override for testing.
 */
function installedBinaryPath(packageDir, opts) {
  const platform = (opts && opts.platform) || process.platform;
  const exe = platform === 'win32' ? 'watermarkremover.exe' : 'watermarkremover';
  return path.join(packageDir, 'bin', exe);
}

/**
 * Resolve the binary path to spawn, in priority order:
 *
 *   1. `WATERMARKREMOVER_BINARY` (when set and the file exists)
 *   2. `<packageDir>/bin/watermarkremover[.exe]` (postinstall target)
 *   3. `watermarkremover` on `$PATH`
 *
 * The result is an object so the caller can tell *which* branch
 * matched (useful for diagnostics and tests).
 *
 * @param {{
 *   env?: NodeJS.ProcessEnv,
 *   packageDir?: string,
 *   pathEnv?: string,
 *   existsSync?: (p: string) => boolean,
 * }} [opts]
 * @returns {{ path: string, source: 'env'|'installed'|'path'|'unresolved' }}
 */
function resolveBinary(opts) {
  const env = (opts && opts.env) || process.env;
  const packageDir = (opts && opts.packageDir) || path.resolve(__dirname, '..');
  const pathEnv = (opts && opts.pathEnv) || env.PATH || '';
  const platform = (opts && opts.platform) || process.platform;
  const existsSync = (opts && opts.existsSync) || fs.existsSync;

  const explicit = env.WATERMARKREMOVER_BINARY;
  if (explicit && existsSync(explicit)) {
    return { path: explicit, source: 'env' };
  }

  const installed = installedBinaryPath(packageDir, { platform });
  if (existsSync(installed)) {
    return { path: installed, source: 'installed' };
  }

  // PATH lookup — minimal re-implementation that respects the
  // platform-specific separator. The Node docs explicitly recommend
  // NOT relying on `which` (it's a shell-out) so we hand-roll it.
  if (pathEnv) {
    const sep = platform === 'win32' ? ';' : ':';
    const exe = platform === 'win32' ? '.exe' : '';
    for (const dir of pathEnv.split(sep)) {
      if (!dir) continue;
      const candidate = path.join(dir, `watermarkremover${exe}`);
      if (existsSync(candidate)) {
        return { path: candidate, source: 'path' };
      }
    }
  }

  return { path: 'watermarkremover', source: 'unresolved' };
}

module.exports = {
  RID_TABLE,
  detectRuntimeId,
  releaseAssetUrl,
  installedBinaryPath,
  resolveBinary,
};
