// Minimal stub for the `astro:env/client` virtual module so the lib code
// can be imported from vitest without spinning up the full Astro runtime.
// Mirrors the schema declared in astro.config.mjs (defaults + reads process.env).

export const PUBLIC_API_URL: string = process.env.PUBLIC_API_URL ?? 'http://localhost:5080';
export const PUBLIC_API_KEY: string = process.env.PUBLIC_API_KEY ?? '';
