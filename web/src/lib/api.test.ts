import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

describe('api', () => {
  const ORIGINAL_ENV = { ...process.env };
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    vi.resetModules();
    delete process.env.PUBLIC_API_URL;
    delete process.env.PUBLIC_API_KEY;
    fetchMock = vi.fn();
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    globalThis.XMLHttpRequest = class {} as unknown as typeof XMLHttpRequest;
  });

  afterEach(() => {
    process.env = { ...ORIGINAL_ENV };
    vi.restoreAllMocks();
  });

  it('cleanText POSTs JSON to /clean/text with correct shape', async () => {
    process.env.PUBLIC_API_URL = 'http://api.example.com';
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          original: 'a',
          cleaned: 'a',
          removedItems: [],
          detections: [],
          confidence: 1,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    const { api } = await import('./api');
    const r = await api.cleanText({ text: 'hello', enableUnicode: true });
    expect(r.cleaned).toBe('a');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://api.example.com/clean/text');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      text: 'hello',
      enableUnicode: true,
    });
    const headers = new Headers(init.headers);
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('sends X-API-Key when PUBLIC_API_KEY is set', async () => {
    process.env.PUBLIC_API_KEY = 's3cret';
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ status: 'ok' }), { status: 200 }),
    );
    const { api } = await import('./api');
    await api.health();
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('X-API-Key')).toBe('s3cret');
  });

  it('throws an ApiError on 4xx with the server message', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ code: 'X', message: 'Bad input' }), { status: 400 }),
    );
    const { api, ApiError } = await import('./api');
    await expect(api.cleanText({ text: 'x' })).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
      message: 'Bad input',
      code: 'X',
    });
    expect(ApiError).toBeDefined();
  });

  it('throws an ApiError on 401 with the server message when no key', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ message: 'Missing or invalid API key.' }), { status: 401 }),
    );
    const { api } = await import('./api');
    await expect(api.cleanText({ text: 'x' })).rejects.toMatchObject({ status: 401 });
  });

  it('cleanMarkdown POSTs JSON with the stripAll flag', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ cleaned: 'x' }), { status: 200 }),
    );
    const { api } = await import('./api');
    await api.cleanMarkdown({ markdown: '# hi', stripAll: true });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain('/clean/markdown');
    expect(JSON.parse(init.body as string)).toEqual({ markdown: '# hi', stripAll: true });
  });

  it('detectText POSTs { text } to /detect/text', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }));
    const { api } = await import('./api');
    await api.detectText('hello');
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain('/detect/text');
    expect(JSON.parse(init.body as string)).toEqual({ text: 'hello' });
  });
});
