# Backlog — WatermarkRemover

Prioritised roadmap of improvements, features, and infrastructure work.
Items move from the roadmap into [TODO.md](./TODO.md) when they're ready
to be picked up by a tick; the tick-ready entry there is the **spec**, this
file is the **why**.

> **Reading order for a new contributor:**
> 1. [README.md](./README.md) — what the tool does and how to install it.
> 2. [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) — module map, data
>    flow, extension points.
> 3. [docs/CONFIGURATION.md](./docs/CONFIGURATION.md) — every `config.yaml`
>    key.
> 4. [TODO.md](./TODO.md) — what's next; items here are tick-ready.

Status legend:
- `[ ]`  — **pending** (not started)
- `[~]`  — **in-progress** (a tick is on it — see [TODO.md](./TODO.md))
- `[x]`  — **done** (completed and committed)
- `[!]`  — **blocked** (waiting on external input / decision)

Each task has a stable ID: `WR-PNNN` (P = priority group). Sprint items
that are tick-ready are mirrored in [TODO.md](./TODO.md) with `WR-SNN` IDs;
the cross-reference is in the `Backlog ref:` line of each TODO entry.

---

## P0 — Release readiness (must-have before v1.0)

### CI/CD & automation
- [x] WR-P001 — GitHub Actions workflow: `build-and-test.yml` (restore, build, test on push/PR; matrix: windows + ubuntu; `dotnet test --logger trx --collect:"XPlat Code Coverage"`; upload coverage to codecov)
- [x] WR-P002 — GitHub Actions workflow: `release.yml` (on tag `v*` — Node 22 + `npm run build` step builds the web UI; `dotnet publish -c Release -r {linux-x64,win-x64,osx-x64,linux-arm64}` self-contained single-file, with `IncludeAllContentForSelfExtract=true` so `wwwroot/` is embedded; zip artifacts; create GitHub Release with attached binaries)
- [x] WR-P003 — `global.json` pinning the SDK version (`10.0.400`) for reproducible builds
- [x] WR-P004 — `Directory.Packages.props` for central package management (→ TODO WR-S5)
- [x] WR-P005 — Dependabot config (`.github/dependabot.yml`) for NuGet + GitHub Actions + Docker

### Containerization
- [x] WR-P006 — `Dockerfile` (multi-stage: `node:22-alpine` webbuild → `mcr.microsoft.com/dotnet/sdk:10.0` build → `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` runtime, non-root user, `EXPOSE 5080`, `HEALTHCHECK CMD curl /health`)
- [x] WR-P007 — `.dockerignore` (exclude `bin/`, `obj/`, `.vs/`, `models/`, `logs/`, `*.user`, `web/node_modules/`, `web/dist/`, `web/.astro/`, `web/.env*`)
- [x] WR-P008 — `docker-compose.yml` (single-service dev compose; volume for models, env for `--api-key`, also `WATERMARKREMOVER_CORS_ORIGINS`)

### Distribution
- [x] WR-P009 — `dotnet publish` publish profiles for self-contained single-file executables (`PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier`, `EnableCompressionInSingleFile=true`, `IncludeAllContentForSelfExtract=true`) — `src/WatermarkRemover.CLI/Properties/PublishProfiles/{linux-x64,linux-arm64,win-x64,osx-x64}.pubxml`
- [ ] WR-P010 — NuGet packaging: mark `WatermarkRemover.Core` / `.Text` / `.Metadata` / `.Image` as `IsPackable=true`, set `PackageId` / `PackageVersion`, `PackageReadmeFile`, `PackageIcon`, create `.snk` for strong naming
- [ ] WR-P011 — `dotnet tool` packaging for a potential global tool install (`dotnet tool install -g watermarkremover`)

### Docs
- [x] WR-P012 — `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`, issue templates, `PULL_REQUEST_TEMPLATE.md`, `CODEOWNERS`, `CODE_OF_CONDUCT.md`, `FUNDING.yml`, `dependabot.yml`
- [x] WR-P013 — `docker-compose.yml`, README overhaul, `docs/FAQ.md`, `docs/COMPARISON.md`, `docs/ARCHITECTURE.md`, `docs/CONFIGURATION.md`, `docs/WEB-UI.md`

---

## P1 — Core features (v1.0)

### New file format support
- [ ] WR-P101 — **TIFF** metadata cleaner (`TIFFMetadataCleaner`) — strip EXIF, IPTC, XMP, ICC; preserve pixel data via `SixLabors.ImageSharp`
- [ ] WR-P102 — **HEIF/HEIC** metadata cleaner — strip EXIF/XMP from Apple's modern photo format
- [x] WR-P103 — **WebP** metadata cleaner — byte-level chunk parser for VP8/VP8L/VP8X RIFF (strip EXIF, XMP, ICC chunks)
- [ ] WR-P104 — **AVIF** metadata cleaner — ISOBMFF box parser for EXIF/XMP
- [ ] WR-P105 — **EPUB** metadata cleaner — strip OPF metadata (creator, contributor, dc:identifier, etc.) via zip-rewrite
- [ ] WR-P106 — **PPTX/XLSX** metadata cleaners — extend OpenXML approach beyond DOCX
- [ ] WR-P107 — **RTF** metadata cleaner — strip `\author`, `\generator`, `\doccomm` control words
- [ ] WR-P108 — **MP4/MOV** metadata cleaner — strip `moov/udta/©xyz` atom (GPS), `udta/meta/keys/ilst` (title/author)

### Text layer enhancements
- [ ] WR-P111 — **DeepSeek / Grok / Mistral** vendor detectors — heuristic patterns for these providers' text watermarks
- [ ] WR-P112 — **Expand synonym dictionary** — increase EN coverage from ~140 → 400+ headwords; RU from ~50 → 200+
- [ ] WR-P113 — **Configurable synonym dictionary** — load custom synonyms from `config.yaml` or an external JSON file
- [ ] WR-P114 — **Additional language synonym sets** — German, French, Spanish, Chinese, Japanese
- [ ] WR-P115 — **Layer B: LLM-based paraphrase mode** — full sentence rewrite (not just token swap) via Ollama endpoint with configurable temperature / top-p
- [ ] WR-P116 — **Token watermark detection** — implement Kirchenbauer green-list entropy analysis (chi-square test on logprob distribution) for text scored by LLMs that expose logprobs

### Image pipeline enhancements
- [ ] WR-P121 — **Additional inpainting backends** — plug-in support for MAT (Mask-Aware Transformer), LaMa-Plus, MI-GAN ONNX models
- [ ] WR-P122 — **Semantic mask via ONNX segmentation** — U2Net or SAM-based watermark detection (detect logos, text overlays by semantic segmentation, not just alpha/color heuristics)
- [ ] WR-P123 — **Manual mask via coordinate spec** — `--mask-rect x,y,w,h` CLI option for precise manual watermark bounding boxes
- [ ] WR-P124 — **Batch image processing** — `clean-image ./images/ --recursive` support (mirror `clean-file` behaviour)
- [ ] WR-P125 — **GPU inference** — ONNX Runtime CUDA/MemoryExecutionProvider auto-detect and use when available
- [ ] WR-P126 — **Tiled inference** — split large images into overlapping 512×512 tiles for models that require fixed resolution, then stitch

### Metadata cleaner enhancements
- [ ] WR-P131 — **C2PA manifest injection** — support `ReplaceC2paManifestPath` option (model exists, implementation TODO) to replace stripped C2PA with a clean manifest
- [ ] WR-P132 — **Stripping IPTC/maker notes config keys** — expose `strip_iptc` and `strip_maker_notes` in `config.yaml` (model supports them, config doesn't surface them)
- [ ] WR-P133 — **PDF form-field metadata** — strip AcroForm / XFA metadata from PDFs
- [ ] WR-P134 — **PDF embedded files** — strip embedded file attachments and their metadata from PDFs

---

## P2 — Platform & UX (v1.x)

### CLI experience
- [ ] WR-P2110 — `watermarkremover --version` command (→ TODO WR-S9)
- [ ] WR-P211 — `clean-all` command (→ TODO WR-S3)
- [ ] WR-P212 — `batch` command — process a JSON/CSV manifest file with list of inputs + desired outputs (for automated pipelines)
- [ ] WR-P213 — `POST /detect/markdown` endpoint (→ TODO WR-S8)
- [ ] WR-P214 — `--quiet` / `-q` global option (suppress all output except errors; useful for scripting)
- [ ] WR-P215 — `--no-color` global option (disable Spectre ANSI; auto-detect non-TTY)
- [ ] WR-P216 — Exit codes documentation (`0` success, `1` input error, `2` detections found, `3` unsupported format, `4` model missing`)
- [ ] WR-P217 — Shell completion scripts generation (→ TODO WR-S6)

### HTTP API enhancements
- [x] WR-P221 — `POST /clean/markdown` endpoint — exists; `ServeCommand.cs:148-158` already implements it. Closed.
- [ ] WR-P222 — `POST /detect/markdown` endpoint (→ TODO WR-S8, same as WR-P213)
- [x] WR-P223 — OpenAPI / Swagger UI at `/swagger` — completed 2026-08-21; spec at `/swagger/v1/swagger.json`, interactive UI at `/swagger/`
- [x] WR-P224 — CORS support (configurable allowed origins via `--cors-origins`)
- [x] WR-P225 — Configurable rate-limit via `config.yaml` (→ TODO WR-S1)
- [x] WR-P226 — File size limit enforcement (configurable `max_upload_mb`, default 100 MB) (→ TODO WR-S2)
- [ ] WR-P227 — `/metrics` endpoint (Prometheus: request count, latency histogram, model availability)
- [x] WR-P228 — Web UI (Astro "box") — single-page plug-and-play dashboard at `/` with Text / Markdown / File / Image tabs. Astro 5.x static output, no UI framework, code-split per tab. Co-located with the .NET binary via `UseStaticFiles` (single-file releases embed the bundle via `IncludeAllContentForSelfExtract`). Standalone deploys (Vercel / Netlify / GH Pages / nginx) also supported. See [`docs/WEB-UI.md`](./docs/WEB-UI.md).

### Configuration
- [ ] WR-P231 — Environment variable overrides (`WATERMARKREMOVER__TEXT__STATISTICAL=true`, double-underscore notation like ASP.NET config)
- [ ] WR-P232 — Full markdown config surface — expose all 21 `MarkdownCleanOptions` toggles in `config.yaml` (only 12 are currently surfaced) (→ TODO WR-S7)
- [ ] WR-P233 — Config validation — fail fast with clear error on unknown keys / invalid values

---

## P3 — Quality & reliability (ongoing)

### Test coverage
- [ ] WR-P311 — `WatermarkRemover.CLI.Tests` project — test CLI commands with `WebApplicationFactory` for HTTP endpoints (→ TODO WR-S4)
- [ ] WR-P312 — Integration tests — end-to-end: create temp files with known watermarks → run CLI → assert cleaned output
- [x] WR-P313 — `WatermarkRemover.Core.Tests` project — test `ConfigLoader`, `AppConfig.Default`, `ErrorResult` — created with 24 tests covering `AppConfig` defaults, `RateLimitConfig`, `MaxUploadMB`
- [ ] WR-P314 — Property-based tests (FsCheck) for Unicode hygiene — random insertion of invisible chars into arbitrary text, assert all removed
- [ ] WR-P315 — Snapshot tests for markdown cleaner — verify output across a corpus of AI-generated markdown samples
- [ ] WR-P316 — Test coverage threshold enforcement in CI (e.g., `coverlet` threshold ≥ 80% line coverage)

### Code quality
- [ ] WR-P321 — Benchmark project (`WatermarkRemover.Benchmarks` with BenchmarkDotNet) — measure throughput of text cleaning, metadata stripping, image inpainting; track regressions in CI
- [ ] WR-P322 — Fix CA1822 (`ProcessCodeSegment` can be static), CA1826 (`Enumerable.First()` on indexable), CA1848/CA1873 (use `LoggerMessage` source generators), CA1711 (`SynonymDictionary` naming), CA1707 (test method underscores), CA1816 (`Dispose` missing `GC.SuppressFinalize`)
- [ ] WR-P323 — `Microsoft.Extensions.Logging` `LoggerMessage` source generator for high-performance logging in Image pipeline
- [ ] WR-P324 — XML doc comments on all public APIs (enable `GenerateDocumentationFile=true` on packable projects; fix all `CS1591` warnings)

### Reliability
- [ ] WR-P331 — Structured error responses in HTTP API (problem+json RFC 9457) instead of ad-hoc `ErrorResult`
- [ ] WR-P332 — Request timeout in HTTP API (configurable, default 30s per request)
- [ ] WR-P333 — Graceful shutdown for `serve` (SIGTERM → drain in-flight requests → close)
- [ ] WR-P334 — Health check endpoint deep mode (`/health?deep=true` → check model availability, disk space, memory)
- [ ] WR-P335 — Resilience in LLM back-translation: Polly retry policy (3 retries, exponential backoff) for transient HTTP failures

---

## P4 — Ecosystem & reach (post-1.0)

### Packaging & distribution
- [ ] WR-P401 — Homebrew tap (`brew install techbuzzz/tap/watermarkremover`) — formula referencing GitHub Release binaries
- [ ] WR-P402 — Scoop bucket (`scoop install watermarkremover`) — Windows package manager manifest
- [ ] WR-P403 — APT/YUM packages for Linux distributions
- [ ] WR-P404 — Winget manifest (`winget install WatermarkRemover`)
- [ ] WR-P405 — Static Linux binary (musl, `linux-musl-x64` RID) for maximum distro compatibility
- [ ] WR-P406 — NuGet packages published to nuget.org — `WatermarkRemover.Core`, `.Text`, `.Metadata`, `.Image` as consumable libraries

### Integrations
- [ ] WR-P411 — Python bindings — `pyo3` or `ctypes` wrapper exposing the text-cleaning pipeline as a Python package (`pip install watermarkremover`)
- [ ] WR-P412 — Node.js bindings — `napi-rs` wrapper for JS/TS consumers
- [ ] WR-P413 — Browser extension — context-menu "Clean AI watermarks" for selected text in Chrome/Firefox (calls local `serve` API)
- [ ] WR-P414 — VS Code extension — right-click "Clean watermarks" on selected text or files
- [ ] WR-P415 — GitHub Action — `uses: techbuzzz/ai-watermark-remover@v1` as a reusable workflow step for CI pipelines
- [ ] WR-P416 — Obsidian plugin — clean AI watermarks from markdown notes

### Research & advanced detection
- [ ] WR-P421 — **Token-level watermark scoring** — implement full Kirchenbauer detection (compute per-token logprob entropy, flag green-list bias via G-test) for text scored by LLMs with exposed logprobs (requires API integration with OpenAI/Anthropic logprob endpoints)
- [ ] WR-P422 — **SynthID-Text detector** — research and implement detection of Google's SynthID-Text statistical watermark (published algorithm: embedding-bias + sampling-bias)
- [ ] WR-P423 — **C2PA manifest reader** — full JUMBF/C2PA box parser to extract and display provenance claims before stripping
- [ ] WR-P424 — **Image forensics** — ELA (Error Level Analysis) detection of AI-generated image regions
- [ ] WR-P425 — **Audio watermarking** — research and implement removal of audio watermarks (if/when AI audio watermarks become standardized)
- [ ] WR-P426 — **Video metadata** — extend to MP4/MOV container metadata and C2PA manifests for video

---

## P5 — Long-term vision

- [ ] WR-P501 — **Real-time API gateway** — managed cloud service (serverless) with auth, rate limiting, billing
- [ ] WR-P502 — **Model marketplace** — community-contributed watermark detection models (ONNX) downloadable via `download-model --list`
- [ ] WR-P503 — **CLI plugin system** — load custom `IFileMetadataCleaner` / `IAiTextWatermarkDetector` implementations from DLLs at runtime
- [ ] WR-P504 — **Web dashboard** — full React/Blazor WASM frontend with batch processing, history, and settings (the current Astro "box" is the minimal v1; this is the v2)
- [ ] WR-P505 — **Telemetry opt-in** — anonymous usage stats (which layers used, file types processed) to guide development priorities
- [ ] WR-P506 — **Multilingual CLI** — localize CLI help strings (Russian, German, Chinese, Japanese)