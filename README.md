<div align="center">

# 🧹 WatermarkRemover

**A cross-platform, single-binary .NET 10 toolkit for stripping AI-provenance watermarks
from text, markdown, files (metadata), and images — with first-class Russian support.**

<p align="center">
  <a href="https://github.com/techbuzzz/ai-watermark-remover/stargazers"><img src="https://img.shields.io/github/stars/techbuzzz/ai-watermark-remover?style=for-the-badge&logo=github&color=ffd33d" alt="GitHub stars" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/network/members"><img src="https://img.shields.io/github/forks/techbuzzz/ai-watermark-remover?style=for-the-badge&logo=github&color=6f42c1" alt="Forks" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/actions/workflows/build-and-test.yml"><img src="https://img.shields.io/github/actions/workflow/status/techbuzzz/ai-watermark-remover/build-and-test.yml?style=for-the-badge&logo=githubactions&label=build%20%26%20test" alt="Build & Test" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/techbuzzz/ai-watermark-remover/release.yml?style=for-the-badge&logo=githubactions&label=release" alt="Release" /></a>
</p>

<p align="center">
  <a href="https://github.com/techbuzzz/ai-watermark-remover/releases/latest"><img src="https://img.shields.io/github/v/release/techbuzzz/ai-watermark-remover?style=for-the-badge&logo=github&color=blue" alt="Latest release" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE"><img src="https://img.shields.io/github/license/techbuzzz/ai-watermark-remover?style=for-the-badge&color=blue" alt="MIT License" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 10" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/pulse"><img src="https://img.shields.io/github/commit-activity/m/techbuzzz/ai-watermark-remover?style=for-the-badge&color=brightgreen" alt="Commit activity" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/issues"><img src="https://img.shields.io/github/issues/techbuzzz/ai-watermark-remover?style=for-the-badge" alt="Open issues" /></a>
  <a href="https://github.com/techbuzzz/ai-watermark-remover/graphs/contributors"><img src="https://img.shields.io/github/contributors/techbuzzz/ai-watermark-remover?style=for-the-badge&color=orange" alt="Contributors" /></a>
</p>

<p align="center">
  <a href="https://github.com/techbuzzz/ai-watermark-remover/issues/new?template=bug-report.yml">🐛 Report a bug</a>
  ·
  <a href="https://github.com/techbuzzz/ai-watermark-remover/issues/new?template=feature-request.yml">✨ Request a feature</a>
  ·
  <a href="./docs/FAQ.md">❓ FAQ</a>
  ·
  <a href="https://github.com/techbuzzz/ai-watermark-remover/discussions">💬 Discussions</a>
</p>

</div>

---

## ✨ Why WatermarkRemover?

AI providers increasingly embed **invisible provenance signals** into the content they
generate — invisible Unicode characters, statistical token biases, hidden C2PA manifests,
EXIF/XMP metadata, visual logos, and per-vendor "SynthID"-style fingerprints. Sometimes
you want to clean your own output, normalize a corpus, or run forensic analysis.

`WatermarkRemover` is a **single .NET 10 binary** (no Node, no Python, no Electron) that:

- 🧽 **Cleans text** through three complementary layers (Unicode hygiene → statistical rewrite → vendor heuristics)
- 📝 **Cleans markdown** while preserving fenced code blocks and configurable structure
- 🗂️ **Strips metadata** from JPEG / PNG / PDF / DOCX / HTML — pixel-preserving, byte-level
- 🖼️ **Inpaints visual watermarks** with the LaMa ONNX model
- 🌐 **Speaks Russian natively** — synonym dictionary, homoglyph-safe Unicode normalisation
- 🚀 **Runs everywhere** — Windows / Linux / macOS / x64 / ARM64
- 🐳 **Hosts as a microservice** — built-in HTTP API with auth, rate limiting, multipart uploads

> ⚠️ **Responsible use.** This tool is for cleaning content **you own** or have permission to
> modify, for security research, and for forensic analysis. Removing watermarks from
> third-party content to evade attribution may violate terms of service or law. See
> [SECURITY.md → Responsible use](./SECURITY.md#responsible-use).

---

## 📑 Table of contents

- [🚀 Quick start](#-quick-start)
- [🌐 Web UI](#-web-ui)
- [📦 Installation](#-installation)
- [🧠 How it works](#-how-it-works)
  - [Text — three layers](#text--three-layers)
  - [Markdown](#markdown)
  - [Metadata](#metadata)
  - [Image](#image)
- [🛠️ CLI reference](#-cli-reference)
- [🌐 HTTP API (`serve`)](#-http-api-serve)
- [🤖 MCP server (`serve-mcp`)](#-mcp-server-serve-mcp)
- [🐳 Docker](#-docker)
- [⚙️ Configuration](#-configuration)
- [🧪 Build & test](#-build--test)
- [🗺️ Solution layout](#-solution-layout)
- [🇷🇺 Поддержка русского языка / Russian language support](#-поддержка-русского-языка--russian-language-support)
- [📚 Documentation](#-documentation)
- [🗓️ Roadmap](#-roadmap)
- [🤝 Contributing](#-contributing)
- [🔐 Security](#-security)
- [📜 License](#-license)
- [💖 Acknowledgements](#-acknowledgements)

---

## 🚀 Quick start

**The promise:** clone, build, run — and the API + web UI are both live on
`http://localhost:5080/`. Or `docker run` and the UI is immediately available
from the container. No extra steps, no separate ports.

```bash
# 1. Clone & build (one-time, needs .NET 10 SDK + Node 20+)
git clone https://github.com/techbuzzz/ai-watermark-remover.git
cd ai-watermark-remover
dotnet build                           # .NET pipeline
(cd web && npm install && npm run build)   # web UI (bundled into the .NET publish)

# 2. Strip invisible characters from a text blob
dotnet run --project src/WatermarkRemover.CLI -- clean-text "Hello‍‎world"   # ZWSP, ZWJ, LRM
# → "Helloworld"

# 3. Strip metadata from a folder of photos (recursive)
dotnet run --project src/WatermarkRemover.CLI -- clean-file ./photos --recursive

# 4. Remove a visual watermark from an image (auto-detected mask + LaMa inpainting)
dotnet run --project src/WatermarkRemover.CLI -- download-model
dotnet run --project src/WatermarkRemover.CLI -- clean-image photo.png -o clean.png

# 5. Spin up the HTTP API + web UI on :5080 (UI at http://localhost:5080/)
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080 --api-key s3cret
```

Or the one-command variants:

```bash
# Linux / macOS (uses Make)
make build && make serve

# Windows (PowerShell)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Serve
```

Or just Docker — single command, UI included:

```bash
docker run --rm -p 5080:5080 techbuzzz/watermarkremover:latest
# → open http://localhost:5080/
```

---

## 🌐 Web UI

`watermarkremover serve` ships a **plug-and-play Astro web UI** on the
same port — a single-page "box" with four tabs (Text, Markdown, File, Image)
that wrap every endpoint in the API. The UI is a pure-static bundle (no
Node server, no framework runtime), so it deploys anywhere a folder of files
can be served.

After building once (see Quick start), the UI is automatically mounted on the
same port as the API:

```bash
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080
# → open http://localhost:5080/
```

To re-point the UI at a different API (e.g. a remote server you already have
running), rebuild with a different URL:

```bash
cd web
PUBLIC_API_URL=https://api.other-host.example.com npm run build
```

For full configuration, dev loop, deployment recipes (Vercel / Netlify / GH
Pages / nginx), and security notes, see **[📘 docs/WEB-UI.md](./docs/WEB-UI.md)**.

---

## 📦 Installation

Every install path produces a working API **and** web UI on the same port.
Pick the path that fits your environment.

### 1. Pre-built binary (recommended)

Download the latest self-contained single-file executable for your platform from
[GitHub Releases](https://github.com/techbuzzz/ai-watermark-remover/releases/latest).
**No .NET runtime required. Web UI is embedded in the binary.**

| Platform    | Architecture | Asset                          |
|-------------|--------------|--------------------------------|
| 🐧 Linux    | x64          | `watermarkremover-linux-x64.zip`     |
| 🐧 Linux    | ARM64        | `watermarkremover-linux-arm64.zip`   |
| 🪟 Windows  | x64          | `watermarkremover-win-x64.zip`       |
| 🍎 macOS    | x64          | `watermarkremover-osx-x64.zip`       |

```bash
# Linux / macOS — one binary, UI included
unzip watermarkremover-linux-x64.zip
sudo mv watermarkremover /usr/local/bin/
watermarkremover serve --port 5080
# → open http://localhost:5080/  (UI + API on the same port)
```

```powershell
# Windows (PowerShell) — one .exe, UI included
Expand-Archive .\watermarkremover-win-x64.zip
& .\watermarkremover\watermarkremover.exe serve --port 5080
# → open http://localhost:5080/  (UI + API on the same port)
```

### 2. Docker (one command, UI included)

```bash
# Build the image (multi-stage; the Astro web UI is built into the same image)
docker build -t watermarkremover .
docker run --rm -p 5080:5080 -e WATERMARKREMOVER_API_KEY=s3cret watermarkremover
# → open http://localhost:5080/
```

Or with `docker compose` (single-service dev loop with the `./models`
volume mounted):

```bash
docker compose up
# → open http://localhost:5080/
```

See [🐳 Docker](#-docker) and [`docker-compose.yml`](./docker-compose.yml).

### 3. From source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Node.js 20+](https://nodejs.org/) (for the web UI only).

```bash
git clone https://github.com/techbuzzz/ai-watermark-remover.git
cd ai-watermark-remover

# One command — builds the web UI + .NET, then runs the server with the UI on :5080
# Linux / macOS
make serve

# Windows PowerShell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Serve
```

Or the manual three-liner if you prefer:

```bash
dotnet build
(cd web && npm install && npm run build)
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080
# → open http://localhost:5080/
```

### 4. As a library (planned)

`WatermarkRemover.Core`, `.Text`, `.Metadata`, `.Image` will be published to NuGet
once the package surface stabilises — see [BACKLOG.md → Distribution](./BACKLOG.md#distribution).
Track progress on the [P1 board](https://github.com/techbuzzz/ai-watermark-remover/milestones).

---

## 🧠 How it works

### Text — three layers

The text pipeline runs three independent layers in order. Each can be toggled via
[`config.yaml`](./src/config.yaml) or the CLI.

- **Layer A · Unicode hygiene** — strips zero-width spaces / joiners, BOM, soft hyphens,
  bidi controls, variation selectors; applies NFKC normalisation and homoglyph folding
  *only* between Latin letters (so genuine Cyrillic text is never mangled).
- **Layer B · Statistical rewrite** — swaps "green-list" tokens for synonyms using built-in
  English **and** Russian dictionaries, or back-translates through an Ollama-compatible
  LLM endpoint for higher-quality rewrites.
- **Layer C · Vendor detectors** — best-effort heuristics for Claude, Gemini/SynthID and
  OpenAI invisible-carrier patterns. These are **heuristic** because the underlying
  schemes are key-based and not publicly verifiable.

### Markdown

20+ toggleable transforms; preserves fenced code blocks (only forced invisible-character
cleanup runs inside them); strips AI-specific artifacts (frontmatter, "As an AI" lines,
emoji-driven sign-offs) when enabled.

### Metadata

Byte-level, pixel-preserving cleaners:

- **JPEG** — segment parser (APP0–APP15, COM); strips EXIF / XMP / IPTC / ICC / C2PA
- **PNG** — chunk filter (tEXt, zTXt, iTXt, eXIf); preserves IHDR / IDAT / IEND
- **PDF** — `PdfPig` rebuild without document info / XMP metadata
- **DOCX** — OpenXML core/extended/custom properties + revision history
- **HTML** — `<meta name="generator|author">` + `<!-- comments -->` + tracking scripts

### Image

`load → mask → resize → infer → blend → save`

- **Mask generation** — alpha + colour-frequency heuristics with connected-component
  extraction (auto-detects logos, text overlays, corner watermarks)
- **Manual mask** — supply your own mask image or coordinates
- **Inference** — `big-lama` ONNX model; degrades gracefully if the model is missing
  (region detection still runs, the source image is copied unchanged)
- **Blending** — alpha-blend the inpainted patch back to keep edges anti-aliased

---

## 🛠️ CLI reference

The produced executable is named `watermarkremover`. During development use
`dotnet run --project src/WatermarkRemover.CLI -- …`.

### Global options (available on every command)

| Flag                | Description                                      |
|---------------------|--------------------------------------------------|
| `--json`            | Emit structured JSON instead of pretty text     |
| `--verbose` / `-v`  | Verbose logging (Debug+)                        |
| `--dry-run`         | Show what *would* happen without touching files |
| `--output` / `-o`   | Write result to a path instead of stdout        |
| `--config` / `-c`   | Path to a custom `config.yaml`                  |
| `--version` / `-V`  | Print the assembly version and exit (short-circuits before config / logging are loaded). A bare `-v` is also accepted; note that Spectre intercepts `-v` even when paired with other args, so use `--verbose` (long form) for verbose logging. |

### Commands

| Command            | Purpose                                                         |
|--------------------|-----------------------------------------------------------------|
| `clean-text`       | Clean plain text (Layers A / B / C).                            |
| `clean-markdown`   | Clean markdown, preserving fenced code blocks.                  |
| `clean-file`       | Strip metadata from files (single / directory / `--recursive`). |
| `clean-image`      | Remove visual watermarks via mask + LaMa inpainting.            |
| `clean-all`        | Auto-route a path — dispatches each file to text / markdown / metadata pipeline by extension (use `--recursive`, `--dry-run`). |
| `detect-text`      | Detect (do not remove) watermark signatures in text.            |
| `detect-markdown`  | Detect AI artifacts in markdown.                                |
| `detect-watermark` | Detect visual watermark regions in an image.                    |
| `inspect-file`     | Report all metadata found in a file.                            |
| `download-model`   | Download & extract the LaMa ONNX inpainting model.              |
| `serve`            | Host the HTTP API (ASP.NET Core Minimal API).                   |
| `serve-mcp`        | Host the MCP server. `--transport stdio` (default, local agents) or `--transport http` (Streamable HTTP, remote). |
| `completions`      | Emit a shell completion script (bash, zsh, powershell, fish).   |

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

# Point one command at a mixed directory: .md → markdown, .jpg → metadata,
# .txt → text, .bin → skipped. Use --dry-run to preview.
dotnet run --project src/WatermarkRemover.CLI -- clean-all ./mixed-dir --recursive

# Inspect a file's metadata (JSON output)
dotnet run --project src/WatermarkRemover.CLI -- inspect-file photo.jpg --json

# Remove a visual watermark (auto-detected mask)
dotnet run --project src/WatermarkRemover.CLI -- clean-image logo.png -o clean.png

# Russian: synonym-aware rewriter is on by default with --statistical
dotnet run --project src/WatermarkRemover.CLI -- clean-text "Это значимый результат." --statistical
# → "Это существенный результат."
```

---

## 🌐 HTTP API (`serve`)

```bash
watermarkremover serve --port 5080 --api-key s3cret
```

- Binds `http://0.0.0.0:5080` by default (override with `--host`, `--port`).
- **Rate limiting:** fixed window, **100 requests / minute / IP** (HTTP 429 when exceeded).
- **Auth:** when `--api-key` is supplied, every endpoint except `/health` requires the
  `X-API-Key` header. Omit `--api-key` to run open.

### Endpoints

| Method & path       | Body                                    | Returns                    |
|---------------------|-----------------------------------------|----------------------------|
| `POST /clean/text`  | `{ "text": "…" }`                       | `TextCleanResult` JSON     |
| `POST /detect/text` | `{ "text": "…" }`                       | `WatermarkMatch[]` JSON    |
| `POST /clean/markdown` | `{ "markdown": "…", "stripAll": false }` | `MarkdownCleanResult` JSON |
| `POST /detect/markdown` | `{ "markdown": "…", "stripAll": false }` | `AiArtifact[]` JSON        |
| `POST /clean/file`  | multipart file upload                   | cleaned file (octet-stream)|
| `POST /inspect/file`| multipart file upload                   | `MetadataEntry[]` JSON     |
| `POST /clean/image` | multipart image upload                  | cleaned image              |
| `POST /detect/image`| multipart image upload                  | `DetectedRegion[]` JSON    |
| `GET  /health`      | —                                       | `{ "status": "ok" }`       |
| `GET  /swagger`     | —                                       | interactive Swagger UI (HTML) |
| `GET  /swagger/v1/swagger.json` | —                          | OpenAPI 3.0 spec (JSON)    |

### Example

```bash
curl -s -X POST http://localhost:5080/clean/text \
  -H "Content-Type: application/json" -H "X-API-Key: s3cret" \
  -d '{"text":"Пример текста"}'```

---

## 🤖 MCP server (`serve-mcp`)

`serve-mcp` exposes the full pipeline as
[Model Context Protocol](https://modelcontextprotocol.io/) tools so any
MCP-compatible agent (Claude Code, OpenCode, MiniMax Code, Cursor,
Continue, …) can call `clean_text`, `clean_markdown`, `clean_file`,
`clean_image`, `detect_text`, `detect_markdown`, `inspect_file`, and
`detect_watermark` directly — no shell-out to the CLI. Built on the
official [`ModelContextProtocol` C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

```bash
# stdio (default) — local agents (Claude Code, OpenCode, MiniMax Code, Cursor, Continue)
watermarkremover serve-mcp
# register with Claude Code
claude mcp add watermarkremover -- watermarkremover serve-mcp

# Streamable HTTP (stateless) — remote agents / Docker
watermarkremover serve-mcp --transport http --port 5090 --api-key s3cret
```

Flags: `--transport <stdio\|http>` (default `stdio`), `-H|--host`,
`-p|--port` (default 5090), `--api-key`, `--rate-limit`,
`--rate-window`. Configurable via the `mcp:` section in
[`config.yaml`](./src/config.yaml) — see
[`docs/CONFIGURATION.md`](./docs/CONFIGURATION.md#mcp). The stdio
transport routes **all** logging to stderr so the JSON-RPC stream
on stdout stays clean, per the [MCP stdio spec](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio).
```

---

## 🐳 Docker

Pre-built images will be published to GHCR / Docker Hub after the first release; until
then, build locally:

```bash
docker build -t watermarkremover .
docker run --rm -p 5080:5080 watermarkremover
# or with API-key auth + a model volume:
docker run --rm -p 5080:5080 \
  -e WATERMARKREMOVER_API_KEY=s3cret \
  -v $(pwd)/models:/app/models \
  watermarkremover
```

A single-service [`docker-compose.yml`](./docker-compose.yml) is provided for the common
dev loop (build, mount `./models`, expose `:5080`).

The `Dockerfile` is **multi-stage**, runs as a **non-root user**, and ships with a
**HEALTHCHECK** that hits `/health`.

---

## ⚙️ Configuration

Copy [`src/config.yaml`](./src/config.yaml) and edit it. Resolution order:

1. `--config <path>` (CLI flag)
2. `./config.yaml` in the working directory
3. `config.yaml` next to the executable
4. Built-in defaults

CLI flags always override config values. See the [Configuration reference](./docs/CONFIGURATION.md)
for every key.

---

## 🧪 Build & test

```bash
dotnet build            # 0 warnings, 0 errors (warnings-as-errors on all src projects)
dotnet test             # 62 tests across Text (35) / Metadata (18) / Image (9)
```

Continuous integration runs on every push / PR to `main` across
**Ubuntu + Windows** runners — see
[`.github/workflows/build-and-test.yml`](./.github/workflows/build-and-test.yml).
Coverage is collected in `cobertura` format and uploaded as a workflow artifact.

---

## 🗺️ Solution layout

```
WatermarkRemover.sln
├── src/
│   ├── WatermarkRemover.Core        # Models, interfaces, configuration, DI contracts
│   ├── WatermarkRemover.Text        # Layer A (Unicode) / B (statistical) / C (vendor) + Markdown
│   ├── WatermarkRemover.Metadata    # JPEG / PNG / PDF / DOCX / HTML metadata cleaners
│   ├── WatermarkRemover.Image       # Mask generation + LaMa ONNX inpainting pipeline
│   └── WatermarkRemover.CLI         # Spectre.Console CLI + ASP.NET Core HTTP API (serve)
└── src/tests/
    ├── WatermarkRemover.Text.Tests       (35 tests)
    ├── WatermarkRemover.Metadata.Tests   (18 tests)
    └── WatermarkRemover.Image.Tests       (9 tests)
```

---

## 🇷🇺 Поддержка русского языка / Russian language support

Приложение полностью поддерживает **русский язык**:

- **Слой A (Unicode)** одинаково безопасно очищает латиницу и кириллицу. Нормализация
  омоглифов (похожих символов) срабатывает **только** когда подозрительный символ стоит
  между латинскими буквами, поэтому настоящие русские слова (например, «Привет мир»)
  никогда не портятся.
- **Слой B (статистический рерайт)** содержит встроенный русский словарь синонимов
  (`SynonymDictionary`) — например, «значимый» → «существенный»,
  «использовать» → «применять». Включается флагом `--statistical`.
- **Слой C (детекторы вендоров)** и **очистка метаданных** не зависят от языка.
- CLI и HTTP API принимают и корректно обрабатывают текст в кодировке UTF-8 на русском
  языке.

Пример:

```bash
dotnet run --project src/WatermarkRemover.CLI -- clean-text "Это значимый результат." --statistical
# → "Это существенный результат."
```

Соответствующие юнит-тесты (`StatisticalWatermarkRewriterTests`, `UnicodeHygieneCleanerTests`,
`MarkdownCleanerTests`, `VendorDetectorTests`) проверяют сохранность и корректную
обработку русского текста.

---

## 📚 Documentation

- 📘 [docs/FAQ.md](./docs/FAQ.md) — frequently asked questions
- ⚖️ [docs/COMPARISON.md](./docs/COMPARISON.md) — how this tool differs from
  `exiftool`, `mat2`, `exiv2`, `sd-webui-watermark`, etc.
- 🏛️ [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) — module boundaries, data flow,
  extension points
- 🌐 [docs/WEB-UI.md](./docs/WEB-UI.md) — the plug-and-play Astro web UI
- ⚙️ [docs/CONFIGURATION.md](./docs/CONFIGURATION.md) — every `config.yaml` key explained
- 🚀 [docs/ci-release.md](./docs/ci-release.md) — how the release pipeline works
- 🧭 [BACKLOG.md](./BACKLOG.md) — prioritised roadmap
- 📝 [TODO.md](./TODO.md) — current sprint

---

## 🗓️ Roadmap

The active roadmap lives in [BACKLOG.md](./BACKLOG.md). Highlights:

**P0 — Release readiness**
- [x] CI matrix on Windows + Linux
- [x] Release workflow (self-contained binaries for 4 RIDs)
- [x] Multi-stage Docker image
- [x] Issue / PR templates, CODEOWNERS, CODE_OF_CONDUCT, SECURITY, CHANGELOG

**P1 — Core features (v1.0)**
- [ ] More metadata formats (WebP, TIFF, HEIF, AVIF, EPUB, RTF, MP4)
- [ ] DeepSeek / Grok / Mistral vendor detectors
- [ ] Synonym dictionary: EN → 400+, RU → 200+
- [ ] Image batch processing + GPU inference
- [ ] OpenAPI / Swagger UI for the HTTP API

**P2 — Platform & UX**
- [ ] `clean-all` auto-routing command
- [ ] Shell completion (PowerShell / bash / zsh / fish)
- [ ] Web UI (vanilla JS drag-and-drop)
- [ ] Configurable rate-limit + CORS + `/metrics`

**P3 — Quality & reliability**
- [ ] CLI integration test project (`WebApplicationFactory`)
- [ ] Property-based tests (FsCheck) for Unicode hygiene
- [ ] BenchmarkDotNet regression suite in CI
- [ ] Polly resilience for LLM back-translation

**P4 — Ecosystem**
- [ ] Homebrew / Scoop / Winget / APT packages
- [ ] Python + Node.js bindings
- [ ] Browser + VS Code extensions
- [ ] NuGet packages

See [BACKLOG.md](./BACKLOG.md) for the full list with status indicators.

---

## 🤝 Contributing

We love PRs. Start with [CONTRIBUTING.md](./CONTRIBUTING.md) for setup, code style,
commit conventions, and the review process. The short version:

1. Fork & branch from `main` (`feat/<short-topic>` or `fix/<short-topic>`).
2. `dotnet build` and `dotnet test` must pass — warnings are errors.
3. Add a unit test for any new behavior; keep the test pyramid balanced.
4. Run `dotnet format` before pushing.
5. Open a PR using the [template](./.github/PULL_REQUEST_TEMPLATE.md); reference any
   related issue with `Closes #N`.

First-time contributors: look for issues tagged
[`good first issue`](https://github.com/techbuzzz/ai-watermark-remover/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22).

Please read our [Code of Conduct](./CODE_OF_CONDUCT.md) — be kind, be constructive.

---

## 🔐 Security

For vulnerabilities, please **do not** file a public issue — see
[SECURITY.md](./SECURITY.md) for the responsible-disclosure channel and our
[responsible-use policy](./SECURITY.md#responsible-use).

---

## 📜 License

[MIT](./LICENSE) — Copyright (c) 2026 Victor Buzin.

---

## 💖 Acknowledgements

- [LaMa](https://github.com/advimman/lama) — LaMa inpainting model (Samsung AI Center)
- [PdfPig](https://github.com/UglyToad/PdfPig) — PDF metadata stripping
- [Spectre.Console](https://spectreconsole.net/) — beautiful CLI
- [ONNX Runtime](https://onnxruntime.ai/) — cross-platform inference
- [FluentAssertions](https://fluentassertions.com/) — readable tests
- Every contributor and stargazer ⭐

---

<div align="center">
  <sub>If WatermarkRemover saved you time, a ⭐ is the easiest way to say thanks.</sub>
</div>
