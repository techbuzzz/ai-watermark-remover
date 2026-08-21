// Thin fetch wrappers for the eight WatermarkRemover HTTP endpoints.
// No third-party deps — `fetch` is universally available in modern browsers.
import { apiBase, apiKey } from './config';

// ---------------------------------------------------------------------------
// Types — mirror the .NET record shapes in src/WatermarkRemover.Core/Models.
// Kept intentionally narrow; only the fields the UI actually renders are typed.
// ---------------------------------------------------------------------------

export interface TextCleanResult {
  original: string;
  cleaned: string;
  removedItems: RemovedItem[];
  detections: WatermarkMatch[];
  confidence: number;
}

export interface RemovedItem {
  type: string;
  position: number;
  length: number;
  description: string;
}

export interface WatermarkMatch {
  vendor: string;
  pattern: string;
  confidence: number;
  position: number;
  length: number;
}

export interface MarkdownCleanResult {
  // The exact shape varies by markdown cleaner version; we render the two
  // string fields and ignore the rest. Use `unknown` for the others to keep
  // the contract loose enough for forwards compatibility.
  original?: string;
  cleaned: string;
  // Any other fields the server returns are accepted but not typed.
  [key: string]: unknown;
}

export interface DetectedRegion {
  x: number;
  y: number;
  width: number;
  height: number;
  confidence: number;
}

export interface MetadataEntry {
  // Loose typing — different formats produce different shapes (EXIF, XMP, etc.).
  [key: string]: unknown;
}

export interface ImageCleanResult {
  inputPath: string;
  outputPath: string;
  detectedWatermarks: DetectedRegion[];
  inputWidth: number;
  inputHeight: number;
  outputWidth: number;
  outputHeight: number;
  processingTime: string; // TimeSpan serialised as e.g. "00:00:01.234"
  modelUsed: string;
}

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

export interface ErrorBody {
  code?: string;
  message?: string;
  [key: string]: unknown;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly code?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

// ---------------------------------------------------------------------------
// JSON request helper
// ---------------------------------------------------------------------------

async function callJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (apiKey) headers.set('X-API-Key', apiKey);
  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const res = await fetch(`${apiBase}${path}`, { ...init, headers });
  return parseResponse<T>(res);
}

async function parseResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const err = await safeReadJson<ErrorBody>(res);
    throw new ApiError(res.status, err?.message ?? res.statusText, err?.code);
  }
  // Some endpoints return octet-stream; caller wraps with `downloadFile` below.
  return (await res.json()) as T;
}

async function safeReadJson<T>(res: Response): Promise<T | null> {
  try {
    return (await res.json()) as T;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Multipart upload helper (XMLHttpRequest, so we get upload progress).
// Returns a tuple: [promise, xhr]. Attach `.upload.onprogress` on xhr.
// ---------------------------------------------------------------------------

export interface UploadHandle<T> {
  promise: Promise<T>;
  xhr: XMLHttpRequest;
  cancel: () => void;
}

export function uploadFile<T>(path: string, file: File): UploadHandle<T> {
  const xhr = new XMLHttpRequest();
  const form = new FormData();
  form.append('file', file);

  const promise = new Promise<T>((resolve, reject) => {
    xhr.open('POST', `${apiBase}${path}`, true);
    if (apiKey) xhr.setRequestHeader('X-API-Key', apiKey);
    xhr.responseType = 'blob';
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        // Caller decides whether the response is JSON or a file blob based
        // on the endpoint contract.
        resolve(xhr.response as unknown as T);
      } else {
        // Try to surface the server's ErrorResult body as a string.
        const blob = xhr.response as Blob | null;
        if (blob && blob.size > 0) {
          blob
            .text()
            .then((text) => {
              let code: string | undefined;
              let message = text;
              try {
                const parsed = JSON.parse(text) as ErrorBody;
                code = parsed.code;
                if (parsed.message) message = parsed.message;
              } catch {
                /* keep raw text */
              }
              reject(new ApiError(xhr.status, message, code));
            })
            .catch(() => reject(new ApiError(xhr.status, xhr.statusText)));
        } else {
          reject(new ApiError(xhr.status, xhr.statusText));
        }
      }
    };
    xhr.onerror = () => reject(new ApiError(0, 'Network error — API unreachable.'));
    xhr.onabort = () => reject(new ApiError(0, 'Cancelled.'));
    xhr.send(form);
  });

  return {
    promise,
    xhr,
    cancel: () => xhr.abort(),
  };
}

// ---------------------------------------------------------------------------
// Endpoint wrappers
// ---------------------------------------------------------------------------

export interface TextRequest {
  text: string;
  enableUnicode?: boolean;
  enableStatistical?: boolean;
  enableVendorSpecific?: boolean;
}

export interface MarkdownRequest {
  markdown: string;
  stripAll?: boolean;
}

export const api = {
  health: () => callJson<{ status: string }>('/health'),

  cleanText: (body: TextRequest) =>
    callJson<TextCleanResult>('/clean/text', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  detectText: (text: string) =>
    callJson<WatermarkMatch[]>('/detect/text', {
      method: 'POST',
      body: JSON.stringify({ text }),
    }),

  cleanMarkdown: (body: MarkdownRequest) =>
    callJson<MarkdownCleanResult>('/clean/markdown', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  cleanFile: (file: File) => uploadFile<Blob>('/clean/file', file),

  inspectFile: (file: File) => uploadFile<Blob>('/inspect/file', file),

  cleanImage: (file: File) => uploadFile<Blob>('/clean/image', file),

  detectImage: (file: File) => uploadFile<Blob>('/detect/image', file),
};

// ---------------------------------------------------------------------------
// Convenience: parse a JSON response from an upload handle (used by
// /inspect/file and /detect/image, which return JSON, not blobs).
// ---------------------------------------------------------------------------

export async function readUploadJson<T>(handle: UploadHandle<Blob>): Promise<T> {
  const blob = await handle.promise;
  const text = await blob.text();
  return JSON.parse(text) as T;
}
