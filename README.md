# WatermarkRemover

A cross-platform **.NET 10** console application that strips AI-provenance watermarks and
metadata from **text**, **files**, and **images**. It removes invisible Unicode steganography,
statistical/green-list token watermarks, vendor-specific signatures (Claude / Gemini / OpenAI),
file metadata (EXIF/XMP/IPTC/C2PA and Office/PDF/HTML properties), and visual image watermarks
via LaMa ONNX inpainting.

> **Language support:** the text pipeline is language-agnostic and ships with **first-class
> Russian (русский) support** — see [Поддержка русского языка](#поддержка-русского-языка--russian-language-support).

---

## Solution layout

```
WatermarkRemover.sln
├── src/
│   ├── WatermarkRemover.Core        # Models, interfaces, configuration, DI contracts
│   ├── WatermarkRemover.Text        # Layer A (Unicode) / B (statistical) / C (vendor) + Markdown
│   ├── WatermarkRemover.Metadata    # JPEG / PNG / PDF / DOCX / HTML metadata cleaners
│   ├── WatermarkRemover.Image       # Mask generation + LaMa ONNX inpainting pipeline
│   └── WatermarkRemover.CLI         # Spectre.Console CLI + ASP.NET Core HTTP API (serve)
└── tests/
    ├── WatermarkRemover.Text.Tests
    ├── WatermarkRemover.Metadata.Tests
    └── WatermarkRemover.Image.Tests
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) the `big-lama` ONNX model for **image** inpainting — download via `download-model`.
  Without it, image cleaning degrades gracefully (detects regions, copies the image unchanged).

## Build & test

```bash
dotnet build            # 0 warnings, 0 errors (warnings-as-errors on all src projects)
dotnet test             # 62 tests across Text / Metadata / Image
```

## Running the CLI

The produced executable is named `watermarkremover`. During development use `dotnet run`:

```bash
dotnet run --project src/WatermarkRemover.CLI -- <command> [options]
```

Global options (available on every command): `--json`, `--verbose`/`-v`, `--dry-run`,
`--output`/`-o <path>`, `--config`/`-c <path>`.

### Commands

| Command            | Purpose                                                        |
|--------------------|---------------------------------------------------------------|
| `clean-text`       | Clean plain text (Layers A/B/C).                              |
| `clean-markdown`   | Clean markdown, preserving fenced code blocks.                |
| `clean-file`       | Strip metadata from files (single / directory / `--recursive`). |
| `clean-image`      | Remove visual watermarks via mask + LaMa inpainting.         |
| `detect-text`      | Detect (not remove) watermark signatures in text.            |
| `detect-markdown`  | Detect AI artifacts in markdown.                             |
| `detect-watermark` | Detect visual watermark regions in an image.                |
| `inspect-file`     | Report all metadata found in a file.                        |
| `download-model`   | Download & extract the LaMa ONNX inpainting model.          |
| `serve`            | Host the HTTP API (ASP.NET Core Minimal API).              |

### Examples

```bash
# Clean text from an argument, a file, or stdin
dotnet run --project src/WatermarkRemover.CLI -- clean-text "Some AI text…"
cat article.txt | dotnet run --project src/WatermarkRemover.CLI -- clean-text --json

# Enable Layer B statistical (green-list) rewriting
dotnet run --project src/WatermarkRemover.CLI -- clean-text -i in.txt -o out.txt --statistical

# Clean markdown but keep everything except AI artifacts (default) or strip everything
dotnet run --project src/WatermarkRemover.CLI -- clean-markdown -i README.md --strip-all

# Strip metadata from a whole folder of files recursively
dotnet run --project src/WatermarkRemover.CLI -- clean-file ./docs --recursive

# Inspect a file's metadata
dotnet run --project src/WatermarkRemover.CLI -- inspect-file photo.jpg --json

# Remove a visual watermark (auto-detected mask)
dotnet run --project src/WatermarkRemover.CLI -- clean-image logo.png -o clean.png
```

## HTTP API (`serve`)

```bash
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080 --api-key "s3cret"
```

- Binds `http://0.0.0.0:5080` by default (`--host`, `--port`).
- **Rate limiting:** fixed window, **100 requests / minute / IP** (HTTP 429 when exceeded).
- **Auth:** when `--api-key` is supplied every endpoint (except `/health`) requires the
  `X-API-Key` header. Omit `--api-key` to run open.

| Method & path      | Body                                    | Returns                    |
|--------------------|-----------------------------------------|----------------------------|
| `POST /clean/text` | `{ "text": "…" }`                       | `TextCleanResult` JSON     |
| `POST /detect/text`| `{ "text": "…" }`                       | `WatermarkMatch[]` JSON    |
| `POST /clean/file` | multipart file upload                   | cleaned file (octet-stream)|
| `POST /inspect/file`| multipart file upload                  | `MetadataEntry[]` JSON     |
| `POST /clean/image`| multipart image upload                  | cleaned image              |
| `POST /detect/image`| multipart image upload                 | `DetectedRegion[]` JSON    |
| `GET  /health`     | —                                       | `{ "status": "ok" }`       |

```bash
curl -s -X POST http://localhost:5080/clean/text \
  -H "Content-Type: application/json" -H "X-API-Key: s3cret" \
  -d '{"text":"Пример текста"}'
```

## How it works

### Text — three layers
- **Layer A · Unicode hygiene** — removes zero-width spaces/joiners, BOM, soft hyphens,
  bidi controls, variation selectors; applies NFKC normalisation and safe homoglyph folding.
- **Layer B · Statistical rewrite** — swaps "green-list" tokens for synonyms (optional LLM
  back-translation through an Ollama-compatible endpoint). English **and** Russian dictionaries.
- **Layer C · Vendor detectors** — best-effort heuristics for Claude, Gemini/SynthID and OpenAI
  invisible-carrier patterns. These are heuristic (the underlying schemes are key-based and not
  publicly verifiable) and are documented as such.

### Markdown
Preserves fenced code blocks (only forced invisible-character cleanup runs inside them) while
applying 20 toggleable transforms and removing AI-specific artifacts and frontmatter.

### Metadata
Byte-level, pixel-preserving cleaners: **JPEG** (segment parser), **PNG** (chunk filter),
**PDF** (PdfPig rebuild without document info/XMP), **DOCX** (OpenXML core/extended/custom
properties + revisions), **HTML** (generator/author meta + comments).

### Image
`load → mask → resize → infer → blend → save`. Masks are auto-generated (alpha + colour-frequency
heuristics with connected-component extraction) or supplied via `--mask`. Inference uses the
`big-lama` ONNX model; when it is missing the pipeline degrades gracefully.

## Configuration

Copy and edit [`config.yaml`](./config.yaml). Resolution order: `--config <path>` →
`./config.yaml` in the working directory → next to the executable → built-in defaults.
CLI flags always override config values.

---

## Поддержка русского языка / Russian language support

Приложение полностью поддерживает **русский язык**:

- **Слой A (Unicode)** одинаково безопасно очищает латиницу и кириллицу. Нормализация
  омоглифов (похожих символов) срабатывает **только** когда подозрительный символ стоит между
  латинскими буквами, поэтому настоящие русские слова (например, «Привет мир») никогда не
  портятся.
- **Слой B (статистический рерайт)** содержит встроенный русский словарь синонимов
  (`SynonymDictionary`) — например, «значимый» → «существенный», «использовать» → «применять».
  Включается флагом `--statistical`.
- **Слой C (детекторы вендоров)** и **очистка метаданных** не зависят от языка.
- CLI и HTTP API принимают и корректно обрабатывают текст в кодировке UTF-8 на русском языке.

Пример:

```bash
dotnet run --project src/WatermarkRemover.CLI -- clean-text "Это значимый результат." --statistical
# → "Это существенный результат."
```

Соответствующие юнит-тесты (`StatisticalWatermarkRewriterTests`, `UnicodeHygieneCleanerTests`,
`MarkdownCleanerTests`, `VendorDetectorTests`) проверяют сохранность и корректную обработку
русского текста.

## Notes & limitations

- Vendor watermark detectors are **heuristic** and best-effort by design.
- The LaMa ONNX model is not bundled; image inpainting requires `download-model` (the upstream
  Hugging Face artifact is a PyTorch checkpoint — the downloader extracts any bundled `.onnx`
  and reports clearly if none is present).
- Test suites are fully self-contained: image tests use a fake inpainting backend, so **no ONNX
  model is required to build or test**.
