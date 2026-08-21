// Small formatting helpers — zero runtime deps, no framework imports.

/** Format a byte count as e.g. "1.2 MB". */
export function formatBytes(n: number): string {
  if (!Number.isFinite(n) || n < 0) return '—';
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`;
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/** Format a millisecond count as e.g. "250 ms" or "1.23 s". */
export function formatMs(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '—';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

/** Format a TimeSpan string from .NET (e.g. "00:00:01.2345678") as a short ms. */
export function formatTimeSpan(ts: string | number | undefined): string {
  if (ts === undefined || ts === null) return '—';
  if (typeof ts === 'number') return `${ts} ms`;
  // Parse the leading hh:mm:ss(.fffffff) portion
  const m = /^(\d+):(\d+):(\d+)(?:\.(\d+))?/.exec(ts);
  if (!m) return ts;
  const [, h, mm, ss, frac] = m;
  const ms =
    Number(h) * 3_600_000 + Number(mm) * 60_000 + Number(ss) * 1000 + (frac ? Number(frac.slice(0, 3).padEnd(3, '0')) : 0);
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

/**
 * Render a line-level diff as HTML. Inserted lines get <ins>, deleted lines
 * get <del>. Returns escaped HTML; safe to insert via innerHTML.
 */
export function diffHtml(a: string, b: string): string {
  const aLines = a.split(/\r?\n/);
  const bLines = b.split(/\r?\n/);
  const out: string[] = [];

  // Simple LCS-based line diff — O(n*m) but n,m are tiny (typical cleaned text
  // is < 200 lines). For larger inputs this could be replaced by a Myers diff.
  const n = aLines.length;
  const m = bLines.length;
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = aLines[i] === bLines[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (aLines[i] === bLines[j]) {
      out.push(escapeHtml(aLines[i]));
      i++;
      j++;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      out.push(`<del>${escapeHtml(aLines[i])}</del>`);
      i++;
    } else {
      out.push(`<ins>${escapeHtml(bLines[j])}</ins>`);
      j++;
    }
  }
  while (i < n) {
    out.push(`<del>${escapeHtml(aLines[i++])}</del>`);
  }
  while (j < m) {
    out.push(`<ins>${escapeHtml(bLines[j++])}</ins>`);
  }
  return out.join('\n');
}

/** Minimal HTML escape for safe innerHTML insertion. */
function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** Truncate a string for display, appending an ellipsis if shortened. */
export function truncate(s: string, max: number): string {
  if (s.length <= max) return s;
  return `${s.slice(0, Math.max(0, max - 1))}…`;
}
