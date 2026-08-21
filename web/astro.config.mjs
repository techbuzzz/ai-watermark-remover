// @ts-check
import { defineConfig, envField } from 'astro/config';

// https://astro.build/config
export default defineConfig({
  output: 'static',
  // Default `site` points at the co-located serve URL; override at build
  // time with `astro build --site https://your-host` when deploying standalone.
  site: 'http://localhost:5080',
  env: {
    schema: {
      PUBLIC_API_URL: envField.string({
        context: 'client',
        access: 'public',
        optional: true,
        default: 'http://localhost:5080',
      }),
      PUBLIC_API_KEY: envField.string({
        context: 'client',
        access: 'public',
        optional: true,
      }),
    },
  },
  vite: {
    build: {
      cssCodeSplit: true,
    },
  },
});
