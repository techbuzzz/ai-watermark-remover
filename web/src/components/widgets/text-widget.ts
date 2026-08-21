// Text-cleaning widget — wires up the form, calls /clean/text and /detect/text.
import { api, ApiError } from '@lib/api';
import { diffHtml, formatMs } from '@lib/format';
import type { TextCleanResult, WatermarkMatch } from '@lib/api';

const $ = <T extends HTMLElement = HTMLElement>(sel: string, root: ParentNode = document) =>
  root.querySelector(sel) as T | null;

export function mountTextWidget(root: HTMLElement): void {
  const form = $('form', root) as HTMLFormElement | null;
  const input = $('textarea[name="text"]', root) as HTMLTextAreaElement | null;
  const cleanBtn = $('button[data-action="clean"]', root) as HTMLButtonElement | null;
  const detectBtn = $('button[data-action="detect"]', root) as HTMLButtonElement | null;
  const resetBtn = $('button[data-action="reset"]', root) as HTMLButtonElement | null;
  const status = $('[data-role="status"]', root);
  const result = $('[data-role="result"]', root) as HTMLElement | null;
  if (!form || !input || !cleanBtn || !detectBtn || !resetBtn || !result) return;

  const setStatus = (msg: string, kind: 'info' | 'error' | 'warn' = 'info') => {
    if (!status) return;
    status.textContent = msg;
    status.dataset.kind = kind;
  };

  const setBusy = (busy: boolean) => {
    [cleanBtn, detectBtn].forEach((b) => (b.disabled = busy));
    form.dataset.busy = String(busy);
  };

  const renderClean = (r: TextCleanResult) => {
    const removed =
      r.removedItems && r.removedItems.length > 0
        ? `<details open><summary>Removed (${r.removedItems.length})</summary><ul>${r.removedItems
            .map(
              (it) =>
                `<li><code>${escape(it.type)}</code> @ ${it.position} (${it.length} chars) — ${escape(it.description)}</li>`,
            )
            .join('')}</ul></details>`
        : '<p class="muted">Nothing removed.</p>';
    const det =
      r.detections && r.detections.length > 0
        ? `<details><summary>Detections (${r.detections.length})</summary><ul>${r.detections
            .map(
              (d) =>
                `<li><strong>${escape(d.vendor)}</strong> — ${escape(d.pattern)} (confidence ${(d.confidence * 100).toFixed(1)}%)</li>`,
            )
            .join('')}</ul></details>`
        : '<p class="muted">No vendor signatures detected.</p>';
    result.innerHTML = `
      <div class="wr-diff">
        <pre>${diffHtml(r.original, r.cleaned)}</pre>
      </div>
      <p class="muted">Confidence: <strong>${(r.confidence * 100).toFixed(1)}%</strong></p>
      ${removed}
      ${det}
    `;
  };

  const renderDetect = (matches: WatermarkMatch[]) => {
    if (matches.length === 0) {
      result.innerHTML = '<p class="muted">No vendor signatures detected.</p>';
      return;
    }
    result.innerHTML = `
      <ul class="wr-list">
        ${matches
          .map(
            (m) =>
              `<li><strong>${escape(m.vendor)}</strong> — ${escape(m.pattern)} @ ${m.position} (${m.length} chars, confidence ${(m.confidence * 100).toFixed(1)}%)</li>`,
          )
          .join('')}
      </ul>
    `;
  };

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const text = input.value;
    if (!text.trim()) {
      setStatus('Paste some text first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Cleaning…');
    const start = performance.now();
    try {
      const opts = Object.fromEntries(new FormData(form).entries()) as Record<string, string>;
      const r = await api.cleanText({
        text,
        enableUnicode: opts.enableUnicode === 'on',
        enableStatistical: opts.enableStatistical === 'on',
        enableVendorSpecific: opts.enableVendorSpecific === 'on',
      });
      renderClean(r);
      setStatus(`Done in ${formatMs(performance.now() - start)}.`);
    } catch (err) {
      setStatus(humanError(err), 'error');
    } finally {
      setBusy(false);
    }
  });

  detectBtn.addEventListener('click', async () => {
    const text = input.value;
    if (!text.trim()) {
      setStatus('Paste some text first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Detecting…');
    const start = performance.now();
    try {
      const matches = await api.detectText(text);
      renderDetect(matches);
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
