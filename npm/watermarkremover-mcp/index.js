#!/usr/bin/env node
/*
 * @watermarkremover/mcp — npm entry point
 * ========================================
 *
 * Spawns the WatermarkRemover MCP server (`watermarkremover serve-mcp`)
 * and pipes stdio. This is the binary the user invokes when they run
 * `npx @watermarkremover/mcp` or wire it into an MCP host.
 *
 * Resolution order for the binary:
 *
 *   1. `WATERMARKREMOVER_BINARY` env var — if it points at an existing
 *      file, use it as-is. Useful for source-mode installs
 *      (`WATERMARKREMOVER_BINARY=/path/to/watermarkremover`) and for
 *      custom-built binaries.
 *   2. `./bin/watermarkremover[.exe]` — the file `postinstall.js` drops
 *      here after fetching the platform-appropriate release artefact
 *      from GitHub Releases.
 *   3. `watermarkremover` on `$PATH` — last-resort fallback so a host
 *      that already has the binary installed still works.
 *
 * The MCP stdio spec requires that **stdout** is the JSON-RPC channel.
 * We therefore inherit the parent's stdio as-is and let the spawned
 * server do its own stderr logging (the server already routes every log
 * level to stderr). See `docs/MCP.md → stdio` for the full contract.
 */

'use strict';

const { spawn } = require('node:child_process');
const { resolveBinary } = require('./lib/binary');

const argv = process.argv.slice(2);

let binaryPath;
try {
  binaryPath = resolveBinary({ env: process.env });
} catch (err) {
  process.stderr.write(
    `@watermarkremover/mcp: failed to locate the WatermarkRemover binary.\n` +
      `  Reason: ${err.message}\n` +
      `  Fix: re-run \`npm install @watermarkremover/mcp\`, set WATERMARKREMOVER_BINARY,\n` +
      `       or install the binary from https://github.com/techbuzzz/ai-watermark-remover/releases\n`,
  );
  process.exit(127);
}

const child = spawn(binaryPath, ['serve-mcp', ...argv], {
  stdio: 'inherit',
  // Detach the child from the parent's controlling terminal where
  // possible. The MCP host usually runs us in a pty already, and
  // forwarding signals is the right behaviour — but if the host is
  // going through `npx`, we want Ctrl-C to propagate to the child.
  windowsHide: true,
});

// Forward common termination signals so the child shuts down cleanly.
for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
  process.on(signal, () => {
    if (!child.killed) {
      child.kill(signal);
    }
  });
}

child.on('error', (err) => {
  process.stderr.write(
    `@watermarkremover/mcp: failed to spawn ${binaryPath}: ${err.message}\n`,
  );
  process.exit(126);
});

child.on('exit', (code, signal) => {
  if (signal) {
    // Propagate the signal exit — Node will set the right code.
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 0);
});
