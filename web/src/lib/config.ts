// Runtime config — single source of truth for the API endpoint and key.
// Reads from astro:env schema (validated at build time).
import { PUBLIC_API_URL, PUBLIC_API_KEY } from 'astro:env/client';

/**
 * Base URL of the WatermarkRemover HTTP API (no trailing slash).
 * Defaults to http://localhost:5080 (the local `serve` default).
 */
export const apiBase: string = (PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/$/, '');

/**
 * Optional API key. If set, every request sends it as `X-API-Key`.
 * NOTE: because these are `PUBLIC_*` env vars, the value is embedded in the
 * built JS. Do not use a sensitive key in a public-internet deploy.
 */
export const apiKey: string | undefined = PUBLIC_API_KEY ? PUBLIC_API_KEY : undefined;

/**
 * Returns true when the current page is running on a trusted host (localhost
 * or a private-network hostname). Used to console-warn when a key is set
 * but the page is being served from a public origin.
 */
export function isTrustedOrigin(): boolean {
  if (typeof window === 'undefined') return true;
  const { hostname, protocol } = window.location;
  if (protocol === 'file:') return true;
  if (hostname === 'localhost' || hostname === '127.0.0.1') return true;
  // Node's URL parser keeps the brackets around IPv6 loopback, browsers drop them.
  if (hostname === '::1' || hostname === '[::1]') return true;
  if (hostname.endsWith('.localhost')) return true;
  if (hostname.endsWith('.local')) return true;
  // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
  if (/^10\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(hostname)) return true;
  if (/^192\.168\.\d{1,3}\.\d{1,3}$/.test(hostname)) return true;
  if (/^172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}$/.test(hostname)) return true;
  return false;
}

if (typeof window !== 'undefined' && apiKey && !isTrustedOrigin()) {
  // eslint-disable-next-line no-console
  console.warn(
    '[watermarkremover-web] PUBLIC_API_KEY is set but the page is being served ' +
      'from a public-looking origin. Remember: PUBLIC_* values are embedded in ' +
      'the JS bundle and are visible to anyone with browser dev-tools.',
  );
}
