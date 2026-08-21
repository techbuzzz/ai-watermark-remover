import { defineConfig } from 'vitest/config';
import { resolve } from 'node:path';

// `astro:env/client` is a virtual module provided by the Astro build.
// In vitest, we substitute a tiny stub that reads from `process.env` so the
// existing tests can exercise the same code paths the browser bundle will.
const astroEnvClientStub = resolve(__dirname, 'test/astro-env-client.stub.ts');

export default defineConfig({
  resolve: {
    alias: {
      'astro:env/client': astroEnvClientStub,
      '@': new URL('./src/', import.meta.url).pathname,
      '@lib': new URL('./src/lib/', import.meta.url).pathname,
      '@components': new URL('./src/components/', import.meta.url).pathname,
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
