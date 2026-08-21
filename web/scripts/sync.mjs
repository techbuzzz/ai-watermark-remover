// Copy the built Astro `dist/` into the .NET CLI's `wwwroot/` so that
// `watermarkremover serve` can serve the UI on the same port as the API.
//
// Run automatically by `npm run build`. Can be re-run on its own with
// `npm run sync`.
//
// The target directory defaults to `<repo>/src/WatermarkRemover.CLI/wwwroot/`
// (the dev path) but can be overridden with the `WR_SYNC_TARGET` env var so
// the Docker build can point it at a known location (e.g. `/out`).

import { cp, mkdir, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const webRoot = resolve(__dirname, '..');
const distDir = join(webRoot, 'dist');
const targetDir = process.env.WR_SYNC_TARGET
  ? resolve(process.env.WR_SYNC_TARGET)
  : resolve(webRoot, '..', 'src', 'WatermarkRemover.CLI', 'wwwroot');

async function main() {
  if (!existsSync(distDir)) {
    console.error(`[sync] no dist/ at ${distDir} — run \`npm run build\` first.`);
    process.exit(1);
  }
  await rm(targetDir, { recursive: true, force: true });
  await mkdir(targetDir, { recursive: true });
  await cp(distDir, targetDir, { recursive: true });
  console.log(`[sync] copied ${distDir} → ${targetDir}`);
}

main().catch((err) => {
  console.error('[sync] failed:', err);
  process.exit(1);
});
