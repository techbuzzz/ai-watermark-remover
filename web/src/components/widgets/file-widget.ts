// File-cleaning widget — calls /clean/file and /inspect/file with upload progress.
import { api, ApiError, readUploadJson } from '@lib/api';
import { formatBytes, formatMs } from '@lib/format';
import type { MetadataEntry } from '@lib/api';

const $ = <T extends HTMLElement = HTMLElement>(sel: string, root: ParentNode = document) =>
  root.querySelector(sel) as T | null;

const MAX_BYTES = 100 * 1024 * 1024; // 100 MB — see BACKLOG P2 file-size limit

export function mountFileWidget(root: HTMLElement): void {
  const drop = $('[data-role="dropzone"]', root) as HTMLElement | null;
  const fileInput = $('input[type="file"]', root) as HTMLInputElement | null;
  const cleanBtn = $('button[data-action="clean"]', root) as HTMLButtonElement | null;
  const inspectBtn = $('button[data-action="inspect"]', root) as HTMLButtonElement | null;
  const resetBtn = $('button[data-action="reset"]', root) as HTMLButtonElement | null;
  const status = $('[data-role="status"]', root);
  const progress = $('[data-role="progress"]', root) as HTMLElement | null;
  const progressBar = $('[data-role="progress-bar"]', root) as HTMLElement | null;
  const fileNameEl = $('[data-role="filename"]', root);
  const fileSizeEl = $('[data-role="filesize"]', root);
  const result = $('[data-role="result"]', root) as HTMLElement | null;
  if (!drop || !fileInput || !cleanBtn || !inspectBtn || !resetBtn || !result) return;

  let currentFile: File | null = null;
  let activeHandle: { cancel: () => void } | null = null;

  const setStatus = (msg: string, kind: 'info' | 'error' | 'warn' = 'info') => {
    if (!status) return;
    status.textContent = msg;
    status.dataset.kind = kind;
  };

  const setProgress = (pct: number) => {
    if (!progress || !progressBar) return;
    progress.dataset.active = pct > 0 && pct < 100 ? 'true' : 'false';
    progressBar.style.width = `${pct.toFixed(0)}%`;
  };

  const setBusy = (busy: boolean) => {
    [cleanBtn, inspectBtn, fileInput].forEach((b) => (b.disabled = busy));
    root.dataset.busy = String(busy);
  };

  const acceptFile = (file: File) => {
    if (file.size > MAX_BYTES) {
      setStatus(`File too large (${formatBytes(file.size)} > 100 MB).`, 'error');
      return;
    }
    currentFile = file;
    if (fileNameEl) fileNameEl.textContent = file.name;
    if (fileSizeEl) fileSizeEl.textContent = formatBytes(file.size);
    setStatus('');
    setProgress(0);
    result.innerHTML = '';
  };

  // Drag-and-drop
  ['dragenter', 'dragover'].forEach((ev) =>
    drop.addEventListener(ev, (e) => {
      e.preventDefault();
      drop.dataset.dragover = 'true';
    }),
  );
  ['dragleave', 'drop'].forEach((ev) =>
    drop.addEventListener(ev, (e) => {
      e.preventDefault();
      drop.dataset.dragover = 'false';
    }),
  );
  drop.addEventListener('drop', (e) => {
    const dt = (e as DragEvent).dataTransfer;
    if (!dt || dt.files.length === 0) return;
    const f = dt.files[0];
    if (f) acceptFile(f);
  });
  drop.addEventListener('click', () => fileInput.click());
  fileInput.addEventListener('change', () => {
    if (fileInput.files && fileInput.files[0]) acceptFile(fileInput.files[0]);
  });

  cleanBtn.addEventListener('click', async () => {
    if (!currentFile) {
      setStatus('Pick a file first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Uploading…');
    setProgress(0);
    const start = performance.now();
    try {
      const handle = api.cleanFile(currentFile);
      activeHandle = handle;
      handle.xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) setProgress((e.loaded / e.total) * 100);
      };
      const blob = await handle.promise;
      activeHandle = null;
      setProgress(100);
      const url = URL.createObjectURL(blob);
      result.innerHTML = `
        <p>Cleaned file ready:</p>
        <a class="wr-button" href="${url}" download="cleaned-${escapeAttr(currentFile.name)}">⬇ cleaned-${escape(currentFile.name)}</a>
        <p class="muted">Done in ${formatMs(performance.now() - start)}.</p>
      `;
      setStatus('Done.');
    } catch (err) {
      setProgress(0);
      setStatus(humanError(err), 'error');
    } finally {
      activeHandle = null;
      setBusy(false);
    }
  });

  inspectBtn.addEventListener('click', async () => {
    if (!currentFile) {
      setStatus('Pick a file first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Uploading…');
    setProgress(0);
    const start = performance.now();
    try {
      const handle = api.inspectFile(currentFile);
      activeHandle = handle;
      handle.xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) setProgress((e.loaded / e.total) * 100);
      };
      const entries = await readUploadJson<MetadataEntry[]>(handle);
      activeHandle = null;
      setProgress(100);
      const list =
        entries.length === 0
          ? '<p class="muted">No metadata found.</p>'
          : `<ul class="wr-list">${entries
              .slice(0, 100)
              .map((e) => `<li><code>${escape(JSON.stringify(e))}</code></li>`)
              .join('')}</ul>${entries.length > 100 ? `<p class="muted">… and ${entries.length - 100} more.</p>` : ''}`;
      result.innerHTML = `<h4>Metadata entries (${entries.length})</h4>${list}`;
      setStatus(`Inspected in ${formatMs(performance.now() - start)}.`);
    } catch (err) {
      setProgress(0);
      setStatus(humanError(err), 'error');
    } finally {
      activeHandle = null;
      setBusy(false);
    }
  });

  resetBtn.addEventListener('click', () => {
    if (activeHandle) activeHandle.cancel();
    currentFile = null;
    fileInput.value = '';
    if (fileNameEl) fileNameEl.textContent = '—';
    if (fileSizeEl) fileSizeEl.textContent = '—';
    result.innerHTML = '';
    setStatus('');
    setProgress(0);
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

function escapeAttr(s: string): string {
  return escape(s);
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
