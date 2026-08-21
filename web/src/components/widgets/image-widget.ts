// Image-cleaning widget — calls /clean/image and /detect/image with progress + preview.
import { api, ApiError, readUploadJson } from '@lib/api';
import { formatBytes, formatMs, formatTimeSpan } from '@lib/format';
import type { DetectedRegion, ImageCleanResult } from '@lib/api';

const $ = <T extends HTMLElement = HTMLElement>(sel: string, root: ParentNode = document) =>
  root.querySelector(sel) as T | null;

const MAX_BYTES = 100 * 1024 * 1024;

export function mountImageWidget(root: HTMLElement): void {
  const drop = $('[data-role="dropzone"]', root) as HTMLElement | null;
  const fileInput = $('input[type="file"]', root) as HTMLInputElement | null;
  const cleanBtn = $('button[data-action="clean"]', root) as HTMLButtonElement | null;
  const detectBtn = $('button[data-action="detect"]', root) as HTMLButtonElement | null;
  const cancelBtn = $('button[data-action="cancel"]', root) as HTMLButtonElement | null;
  const resetBtn = $('button[data-action="reset"]', root) as HTMLButtonElement | null;
  const status = $('[data-role="status"]', root);
  const progress = $('[data-role="progress"]', root) as HTMLElement | null;
  const progressBar = $('[data-role="progress-bar"]', root) as HTMLElement | null;
  const fileNameEl = $('[data-role="filename"]', root);
  const fileSizeEl = $('[data-role="filesize"]', root);
  const preview = $('[data-role="preview"]', root) as HTMLImageElement | null;
  const result = $('[data-role="result"]', root) as HTMLElement | null;
  if (!drop || !fileInput || !cleanBtn || !detectBtn || !cancelBtn || !resetBtn || !result) return;

  let currentFile: File | null = null;
  let currentObjectUrl: string | null = null;
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
    [cleanBtn, detectBtn, fileInput, cancelBtn].forEach((b) => (b.disabled = busy));
    cancelBtn.disabled = !busy; // cancel only enabled while busy
    root.dataset.busy = String(busy);
  };

  const acceptFile = (file: File) => {
    if (!/^image\//.test(file.type) && !/\.(png|jpe?g|webp|gif|bmp)$/i.test(file.name)) {
      setStatus('Not an image file.', 'error');
      return;
    }
    if (file.size > MAX_BYTES) {
      setStatus(`File too large (${formatBytes(file.size)} > 100 MB).`, 'error');
      return;
    }
    currentFile = file;
    if (fileNameEl) fileNameEl.textContent = file.name;
    if (fileSizeEl) fileSizeEl.textContent = formatBytes(file.size);
    if (currentObjectUrl) URL.revokeObjectURL(currentObjectUrl);
    currentObjectUrl = URL.createObjectURL(file);
    if (preview) {
      preview.src = currentObjectUrl;
      preview.hidden = false;
    }
    setStatus('');
    setProgress(0);
    result.innerHTML = '';
  };

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

  const attachProgress = (handle: { xhr: XMLHttpRequest }) => {
    handle.xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) setProgress((e.loaded / e.total) * 100);
    };
  };

  cleanBtn.addEventListener('click', async () => {
    if (!currentFile) {
      setStatus('Pick an image first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Uploading + inpainting…');
    setProgress(0);
    const start = performance.now();
    try {
      const handle = api.cleanImage(currentFile);
      activeHandle = handle;
      attachProgress(handle);
      const blob = (await handle.promise) as Blob;
      activeHandle = null;
      setProgress(100);
      const url = URL.createObjectURL(blob);
      const meta = blob.size;
      result.innerHTML = `
        <div class="wr-image-result">
          <img alt="Cleaned" src="${url}" />
          <p>
            <a class="wr-button" href="${url}" download="cleaned-${escapeAttr(currentFile.name)}">⬇ Download cleaned</a>
            <span class="muted">(${formatBytes(meta)})</span>
          </p>
        </div>
      `;
      setStatus(`Done in ${formatMs(performance.now() - start)}.`);
    } catch (err) {
      setProgress(0);
      setStatus(humanError(err), 'error');
    } finally {
      activeHandle = null;
      setBusy(false);
    }
  });

  detectBtn.addEventListener('click', async () => {
    if (!currentFile) {
      setStatus('Pick an image first.', 'warn');
      return;
    }
    setBusy(true);
    setStatus('Uploading + detecting…');
    setProgress(0);
    const start = performance.now();
    try {
      const handle = api.detectImage(currentFile);
      activeHandle = handle;
      attachProgress(handle);
      const regions = await readUploadJson<DetectedRegion[]>(handle);
      activeHandle = null;
      setProgress(100);
      if (regions.length === 0) {
        result.innerHTML = '<p class="muted">No watermark regions detected.</p>';
      } else {
        result.innerHTML = `
          <h4>Detected regions (${regions.length})</h4>
          <ul class="wr-list">
            ${regions
              .map(
                (r) =>
                  `<li>(${r.x}, ${r.y}) — ${r.width}×${r.height} px · confidence ${(r.confidence * 100).toFixed(1)}%</li>`,
              )
              .join('')}
          </ul>
        `;
      }
      setStatus(`Detected in ${formatMs(performance.now() - start)}.`);
    } catch (err) {
      setProgress(0);
      setStatus(humanError(err), 'error');
    } finally {
      activeHandle = null;
      setBusy(false);
    }
  });

  cancelBtn.addEventListener('click', () => {
    if (activeHandle) activeHandle.cancel();
  });

  resetBtn.addEventListener('click', () => {
    if (activeHandle) activeHandle.cancel();
    currentFile = null;
    fileInput.value = '';
    if (fileNameEl) fileNameEl.textContent = '—';
    if (fileSizeEl) fileSizeEl.textContent = '—';
    if (preview) {
      preview.removeAttribute('src');
      preview.hidden = true;
    }
    if (currentObjectUrl) {
      URL.revokeObjectURL(currentObjectUrl);
      currentObjectUrl = null;
    }
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

// Suppress the unused import warning for ImageCleanResult / formatTimeSpan —
// they are re-exported for the bundled output and used by the inline preview
// templates above.
export type { ImageCleanResult };
export { formatTimeSpan };
