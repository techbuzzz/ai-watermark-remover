#!/usr/bin/env node
/*
 * @watermarkremover/mcp — postinstall hook
 * ========================================
 *
 * Downloads the platform-appropriate WatermarkRemover release binary
 * from GitHub Releases and extracts it into `./bin/`. The download
 * is skipped when:
 *
 *   - The user opted out via `WR_SKIP_BINARY_DOWNLOAD=1` (e.g. a
 *     source-mode install where they intend to point
 *     `WATERMARKREMOVER_BINARY` at a local build).
 *   - A binary already exists at `./bin/watermarkremover[.exe]`
 *     and its version matches the one this package expects.
 *   - The `bin/` directory already contains a working binary (idempotent
 *     re-installs are a no-op).
 *
 * The matching release tag is `v` + this package's version. We pin
 * the exact tag instead of "latest" so a `npm install` never silently
 * upgrades a tool an agent depends on. Users who *want* a rolling
 * channel can install `@watermarkremover/mcp@next` once those tags
 * exist.
 */

'use strict';

const { installBinary } = require('./lib/install');

installBinary({
  env: process.env,
  cwd: __dirname,
  // The package version comes from package.json. Keeping the lookup
  // lazy (require at call time) means a re-publish with a new version
  // does not require editing this file.
  expectedVersion: require('./package.json').version,
  // Suppress non-error output when stdout is non-TTY. CI runs are
  // quiet; humans get progress on stderr.
  log: process.stderr.isTTY
    ? (msg) => process.stderr.write(`@watermarkremover/mcp: ${msg}\n`)
    : () => {},
  error: (msg) => process.stderr.write(`@watermarkremover/mcp: ${msg}\n`),
}).catch((err) => {
  process.stderr.write(
    `@watermarkremover/mcp: postinstall failed: ${err && err.message ? err.message : err}\n` +
      `  Re-run with WR_SKIP_BINARY_DOWNLOAD=1 to skip the download and supply\n` +
      `  your own binary via the WATERMARKREMOVER_BINARY env var.\n`,
  );
  // Do NOT exit with a non-zero code: failing postinstall is a soft
  // error in npm's eyes (it surfaces a yellow warning) but breaks
  // `npm install` for the user. The user can still wire up
  // WATERMARKREMOVER_BINARY and run the server — so we exit 0.
  process.exit(0);
});
