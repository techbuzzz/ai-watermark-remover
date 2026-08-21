import { describe, expect, it } from 'vitest';
import { diffHtml, formatBytes, formatTimeSpan, truncate } from './format';

describe('formatBytes', () => {
  it('formats bytes', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1024)).toBe('1.0 KB');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB');
    expect(formatBytes(1024 * 1024 * 1024)).toBe('1.00 GB');
  });

  it('handles bad input gracefully', () => {
    expect(formatBytes(Number.NaN)).toBe('—');
    expect(formatBytes(-1)).toBe('—');
  });
});

describe('formatTimeSpan', () => {
  it('parses .NET TimeSpan strings', () => {
    expect(formatTimeSpan('00:00:00.5000000')).toBe('500 ms');
    expect(formatTimeSpan('00:00:01.2340000')).toBe('1.23 s');
    expect(formatTimeSpan(750)).toBe('750 ms');
  });

  it('falls back to the raw value for unrecognised strings', () => {
    expect(formatTimeSpan('not a timespan')).toBe('not a timespan');
    expect(formatTimeSpan(undefined)).toBe('—');
  });
});

describe('diffHtml', () => {
  it('returns identical content for identical input', () => {
    const out = diffHtml('hello\nworld', 'hello\nworld');
    expect(out).toContain('hello');
    expect(out).toContain('world');
    expect(out).not.toContain('<ins>');
    expect(out).not.toContain('<del>');
  });

  it('marks inserted and deleted lines', () => {
    const out = diffHtml('a\nb', 'a\nc');
    expect(out).toContain('<del>b</del>');
    expect(out).toContain('<ins>c</ins>');
  });

  it('escapes HTML in input', () => {
    const out = diffHtml('<script>', '<safe>');
    expect(out).toContain('&lt;script&gt;');
    expect(out).toContain('&lt;safe&gt;');
    expect(out).not.toContain('<script>');
  });
});

describe('truncate', () => {
  it('passes through short strings', () => {
    expect(truncate('hi', 10)).toBe('hi');
  });
  it('truncates long strings with an ellipsis', () => {
    expect(truncate('hello world', 6)).toBe('hello…');
    expect(truncate('hello world', 5)).toBe('hell…');
  });
});
