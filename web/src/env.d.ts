/// <reference path="../.astro/types.d.ts" />
/// <reference types="astro/client" />

interface ImportMetaEnv {
  readonly PUBLIC_API_URL?: string;
  readonly PUBLIC_API_KEY?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
