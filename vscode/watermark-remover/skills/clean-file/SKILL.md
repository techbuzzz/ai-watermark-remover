---
name: watermark-clean-file
description: >
  Use when the user uploads a JPEG, PNG, PDF, DOCX, HTML, or WebP file
  and asks to strip metadata (EXIF, XMP, IPTC, C2PA, XMP-pdf, DOCX
  core properties, HTML meta tags) before sharing. Calls the
  WatermarkRemover `clean_file` / `inspect_file` MCP tools or pipes the
  file through `watermarkremover clean-file`.
license: MIT
compatibility: opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-clean-file`

Byte-level metadata stripper. **Pixel-preserving for images** — only
metadata chunks (EXIF / XMP / IPTC for JPEG, tEXt / iTXt / zTXt for
PNG, XMP-pdf + Info dict for PDF, core.xml for DOCX, `<meta>` for
HTML) are removed. The file is rewritten from the same bytes minus the
metadata; no re-encoding, no quality loss.

## When to use

Activate this skill when the user:

- Uploads a photo and says "strip the metadata" / "remove EXIF" / "I
  don't want my GPS coordinates in this"
- Mentions C2PA, "content credentials", "provenance manifest"
- Wants to share a DOCX / PDF and asks to "anonymize" it
- Asks what metadata is in a file before deciding whether to clean it
  (use `inspect_file` first)
- Has a folder of mixed-format files and wants them all cleaned at
  once (use `clean-all` CLI command or call `clean_file` in a loop)

Do **not** use this skill for:

- Plain text or Markdown (use `watermark-clean-text` /
  `watermark-clean-markdown`)
- Visual watermarks / logos embedded in the image pixels (use
  `watermark-clean-image` — that's a different pipeline, runs LaMa
  inpainting and is *not* pixel-preserving)
- Detection only (use `watermark-detect`)

## File-type → cleaner mapping

| Extension(s)        | Cleaner               | Metadata chunks handled                                          |
|---------------------|-----------------------|-------------------------------------------------------------------|
| `.jpg`, `.jpeg`     | `JpegMetadataCleaner` | EXIF (app1), XMP (app1), IPTC (app13), ICC profile (optional)    |
| `.png`              | `PngMetadataCleaner`  | tEXt, zTXt, iTXt, eXIf, tIME, C2PA / c2pa (claim), iCCP (optional) |
| `.webp`             | `WebPMetadataCleaner` | EXIF, XMP, ICCP                                                  |
| `.pdf`              | `PdfMetadataCleaner`  | XMP-pdf metadata stream, Info dictionary, /Author /Producer       |
| `.docx`             | `DocxMetadataCleaner` | docProps/core.xml, docProps/app.xml, custom.xml                   |
| `.html`, `.htm`     | `HtmlMetadataCleaner` | `<meta name="…">`, `<meta property="og:…">`, Open Graph, Twitter cards |

Unsupported extensions are passed through the text pipeline
(`ITextCleaningPipeline`) — so a `.txt` file routed through `clean_file`
still gets the same Unicode hygiene as `clean_text`.

## Tool call

Inspect first (optional but recommended):

```json
{
  "name": "inspect_file",
  "arguments": { "path": "/tmp/photo.jpg" }
}
```

Response:

```json
{
  "path": "/tmp/photo.jpg",
  "size": 2485392,
  "metadata": [
    { "kind": "EXIF",  "tag": "Make",          "value": "Apple" },
    { "kind": "EXIF",  "tag": "Model",         "value": "iPhone 15" },
    { "kind": "EXIF",  "tag": "GPSLatitude",   "value": "37.3349° N" },
    { "kind": "C2PA",  "tag": "claim",         "value": "urn:uuid:…" }
  ]
}
```

Then clean:

```json
{
  "name": "clean_file",
  "arguments": { "path": "/tmp/photo.jpg" }
}
```

Response is the cleaned bytes as an `EmbeddedResourceBlock` with the
correct MIME (`image/jpeg`, `application/pdf`, …). For PDFs, the
metadata strip is **without** rewriting the byte stream layout — same
file size class, just without the metadata blocks.

CLI fallback:

```bash
watermarkremover clean-file photo.jpg            # in-place
watermarkremover clean-file photo.jpg -o clean.jpg
watermarkremover inspect-file photo.jpg          # JSON report on stdout
```

## Examples

### 1. Strip GPS from a JPEG before posting online

User: "Strip the EXIF from this photo before I post it on Mastodon."

1. `inspect_file` → reports `GPSLatitude`, `GPSLongitude`, camera model.
2. `clean_file` → returns the same image without any EXIF / XMP / IPTC.

### 2. Anonymize a PDF for a public release

User: "I need to share this report PDF publicly but I don't want the
author / creator / last-modified-by in there."

1. `inspect_file` → shows `/Author`, `/Creator`, `/Producer`,
   XMP-pdf `dc:creator`.
2. `clean_file` → returns the PDF with those entries blanked (Info
   dictionary keys are kept but cleared; the XMP stream is removed).

### 3. Drop C2PA from a PNG

User: "This PNG has a C2PA claim in it, strip it."

`clean_file` returns the PNG without the `c2pa` chunk. All other
chunks (including pixel data) are preserved.

## Error handling

- **Unsupported extension** — `clean_file` returns a tool error. Tell
  the user the format is not yet supported (see roadmap / `BACKLOG.md`).
- **File not found / permission denied** — the wrapper exits non-zero
  with a clear OS error; surface it verbatim.
- **ICC profile wanted preserved** — by default the ICC colour profile
  is preserved; pass `--strip-icc` to drop it too. The agent should
  only do this if the user explicitly asks.

## Reference

- MCP tool reference:
  [docs/MCP.md → `clean_file`](../../docs/MCP.md#clean_file),
  [`inspect_file`](../../docs/MCP.md#inspect_file)
- Architecture: [docs/ARCHITECTURE.md → Metadata cleaning](../../docs/ARCHITECTURE.md#metadata-cleaning-clean-file)
- Format roadmap: `BACKLOG.md → P1` (WebP, TIFF, HEIF, AVIF, EPUB, RTF, MP4)
