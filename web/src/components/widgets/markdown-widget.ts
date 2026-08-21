// Markdown-cleaning widget — calls /clean/markdown.
import { api, ApiError } from '@lib/api';
import { formatMs } from '@lib/format';
import type { MarkdownCleanResult } from '@lib/api';

const $ = <T extends HTMLElement = HTMLElement>(sel: string, root: ParentNode = document) =>
  root.querySelector(sel) as T | null;

export function mountMarkdownWidget(root: HTMLElement): void {
  const form = $('form', root) as HTMLFormElement | null;
  const input = $('textarea[name="markdown"]', root) as HTMLTextAreaElement | null;
  const submitBtn = $('button[type="submit"]', root) as HTMLButtonElement | null;
  const resetBtn = $('button[data-action="reset"]', root) as HTMLButtonElement | null;
  const status = $('[data-role="status"]', root);
  const result = $('[data-role="result"]', root) as HTMLElement | null;
  if (!form || !input || !submitBtn || !resetBtn || !result) return;

  const setStatus = (msg: string, kind: 'info' | 'error' | 'warn' = 'info') => {
    if (!status) return;
    status.textContent = msg;
    status.dataset.kind = kind;
  };

  const setBusy = (busy: boolean) => {
    submitBtn.disabled = busy;
    form.dataset.busy = String(busy);
  };

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const markdown = input.value;
    if (!markdown.trim()) {
      setStatus('Paste some markdown first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Cleaning…');
    const start = performance.now();
    try {
      const stripAll = (form.querySelector('input[name="stripAll"]') as HTMLInputElement | null)?.checked ?? false;
      const r: MarkdownCleanResult = await api.cleanMarkdown({ markdown, stripAll });
      result.innerHTML = `
        <h4>Cleaned</h4>
        <pre class="wr-output">${escape(r.cleaned)}</pre>
        <p class="muted">Length: ${r.cleaned.length.toLocaleString()} chars${stripAll ? ' (strip-all mode)' : ''}.</p>
      `;
      setStatus(`Done in ${formatMs(performance.now() - start)}.`);
    } catch (err) {
      setStatus(humanError(err), 'error');
    } finally {
      setBusy(false);
    }
  });

  resetBtn.addEventListener('click', () => {
    form.reset();
    result.innerHTML = '';
    setStatus('');
  });
}

function escape(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function humanError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 0) return err.message;
    if (err.status === 401) return 'API key required. Set PUBLIC_API_KEY or run the API without --api-key.';
    if (err.status === 429) return 'Rate limited. Please wait a moment and try again.';
    return `${err.status} — ${err.message}`;
  }
  if (err instanceof Error) return err.message;
  return String(err);
}
