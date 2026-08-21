import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

describe('config', () => {
  const ORIGINAL_ENV = { ...process.env };

  beforeEach(() => {
    vi.resetModules();
    delete process.env.PUBLIC_API_URL;
    delete process.env.PUBLIC_API_KEY;
  });

  afterEach(() => {
    process.env = { ...ORIGINAL_ENV };
  });

  it('defaults apiBase to localhost:5080 and apiKey to undefined', async () => {
    const { apiBase, apiKey } = await import('./config');
    expect(apiBase).toBe('http://localhost:5080');
    expect(apiKey).toBeUndefined();
  });

  it('reads PUBLIC_API_URL and trims trailing slashes', async () => {
    process.env.PUBLIC_API_URL = 'https://api.example.com/';
    const { apiBase } = await import('./config');
    expect(apiBase).toBe('https://api.example.com');
  });

  it('reads PUBLIC_API_KEY when set', async () => {
    process.env.PUBLIC_API_KEY = 's3cret';
    const { apiKey } = await import('./config');
    expect(apiKey).toBe('s3cret');
  });

  it('treats an empty PUBLIC_API_KEY as undefined', async () => {
    process.env.PUBLIC_API_KEY = '';
    const { apiKey } = await import('./config');
    expect(apiKey).toBeUndefined();
  });
});

describe('isTrustedOrigin', () => {
  it('returns true for localhost and private-network hostnames', async () => {
    const { isTrustedOrigin } = await import('./config');
    const cases: Array<[string, boolean]> = [
      ['http://localhost:4321', true],
      ['http://127.0.0.1:4321', true],
      ['http://[::1]:4321', true],
      ['http://host.local:4321', true],
      ['http://10.0.0.5:4321', true],
      ['http://192.168.1.5:4321', true],
      ['http://172.16.0.5:4321', true],
      ['http://172.31.255.5:4321', true],
      ['https://example.com', false],
      ['https://api.public.io', false],
    ];
    for (const [url, expected] of cases) {
      globalThis.window = { location: new URL(url) } as unknown as Window & typeof globalThis;
      expect(isTrustedOrigin()).toBe(expected);
    }
  });
});
