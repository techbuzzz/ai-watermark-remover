# Backlog — WatermarkRemover

Prioritized list of improvements, features, and infrastructure work to make
WatermarkRemover the most popular AI-watermark-removal tool.

Status legend: **planned** · **in-progress** · **done**

---

## P0 — Release readiness (must-have before v1.0)

### CI/CD & automation
- [ ] planned — GitHub Actions workflow: `build-and-test.yml` (restore, build, test on push/PR; matrix: windows + ubuntu; `dotnet test --logger trx --collect:"XPlat Code Coverage"`; upload coverage to codecov)
- [ ] planned — GitHub Actions workflow: `release.yml` (on tag `v*` — `dotnet publish -c Release -r {linux-x64,win-x64,osx-x64,linux-arm64}` self-contained, zip artifacts, create GitHub Release with attached binaries)
- [ ] planned — `global.json` pinning the SDK version (`10.0.400`) for reproducible builds
- [ ] planned — `Directory.Packages.props` for central package management (single version source for all NuGet refs)
- [ ] planned — Dependabot config (`.github/dependabot.yml`) for NuGet + GitHub Actions updates

### Containerization
- [ ] planned — `Dockerfile` (multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` build → `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` runtime, non-root user, `EXPOSE 5080`, `HEALTHCHECK CMD curl /health`)
- [ ] planned — `.dockerignore` (exclude `bin/`, `obj/`, `.vs/`, `models/`, `logs/`, `*.user`)
- [ ] planned — `docker-compose.yml` (single-service dev compose; volume for models, env for `--api-key`)

### Distribution
- [ ] planned — `dotnet publish` publish profiles for self-contained single-file executables (`PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier`, `EnableCompressionInSingleFile=true`)
- [ ] planned — NuGet packaging: mark `WatermarkRemover.Core` / `.Text` / `.Metadata` / `.Image` as `IsPackable=true`, set `PackageId` / `PackageVersion`, `PackageReadmeFile`, `PackageIcon`, create `.snk` for strong naming
- [ ] planned — `dotnet tool` packaging for a potential global tool install (`dotnet tool install -g watermarkremover`)

### Docs
- [ ] planned — `CONTRIBUTING.md` (build/test instructions, code style, PR process, commit conventions)
- [ ] planned — `SECURITY.md` (responsible disclosure for a tool that removes tracking watermarks)
- [ ] planned — `CHANGELOG.md` (Keep a Changelog format)
- [ ] planned — GitHub issue templates: `.github/ISSUE_TEMPLATE/bug-report.yml` + `feature-request.yml`
- [ ] planned — `.github/PULL_REQUEST_TEMPLATE.md`
- [ ] planned — `.github/CODEOWNERS`
- [ ] planned — `CODE_OF_CONDUCT.md` (Contributor Covenant)

---

## P1 — Core features (v1.0)

### New file format support
- [ ] planned — **TIFF** metadata cleaner (`TIFFMetadataCleaner`) — strip EXIF, IPTC, XMP, ICC; preserve pixel data via `SixLabors.ImageSharp`
- [ ] planned — **HEIF/HEIC** metadata cleaner — strip EXIF/XMP from Apple's modern photo format
- [ ] planned — **WebP** metadata cleaner — byte-level chunk parser for VP8/VP8L/VP8X RIFF (strip EXIF, XMP, ICC chunks)
- [ ] planned — **AVIF** metadata cleaner — ISOBMFF box parser for EXIF/XMP
- [ ] planned — **EPUB** metadata cleaner — strip OPF metadata (creator, contributor, dc:identifier, etc.) via zip-rewrite
- [ ] planned — **PPTX/XLSX** metadata cleaners — extend OpenXML approach beyond DOCX
- [ ] planned — **RTF** metadata cleaner — strip `\author`, `\generator`, `\doccomm` control words
- [ ] planned — **MP4/MOV** metadata cleaner — strip `moov/udta/©xyz` atom (GPS), `udta/meta/keys/ilst` (title/author)

### Text layer enhancements
- [ ] planned — **DeepSeek vendor detector** — heuristic patterns for DeepSeek text watermarks
- [ ] planned — **Grok/xAI vendor detector** — detect Grok-specific artifacts
- [ ] planned — **Mistral vendor detector** — detect Mistral-specific artifacts
- [ ] planned — **Expand synonym dictionary** — increase EN coverage from ~140 → 400+ headwords; RU from ~50 → 200+
- [ ] planned — **Configurable synonym dictionary** — load custom synonyms from `config.yaml` or an external JSON file
- [ ] planned — **Additional language synonym sets** — German, French, Spanish, Chinese, Japanese
- [ ] planned — **Layer B: LLM-based paraphrase mode** — full sentence rewrite (not just token swap) via Ollama endpoint with configurable temperature / top-p
- [ ] planned — **Token watermark detection** — implement Kirchenbauer green-list entropy analysis (chi-square test on logprob distribution) for text scored by LLMs that expose logprobs

### Image pipeline enhancements
- [ ] planned — **Additional inpainting backends** — plug-in support for MAT (Mask-Aware Transformer), LaMa-Plus, MI-GAN ONNX models
- [ ] planned — **Semantic mask via ONNX segmentation** — U2Net or SAM-based watermark detection (detect logos, text overlays by semantic segmentation, not just alpha/color heuristics)
- [ ] planned — **Manual mask via coordinate spec** — `--mask-rect x,y,w,h` CLI option for precise manual watermark bounding boxes
- [ ] planned — **Batch image processing** — `clean-image ./images/ --recursive` support (mirror `clean-file` behaviour)
- [ ] planned — **GPU inference** — ONNX Runtime CUDA/MemoryExecutionProvider auto-detect and use when available
- [ ] planned — **Tiled inference** — split large images into overlapping 512×512 tiles for models that require fixed resolution, then stitch

### Metadata cleaner enhancements
- [ ] planned — **C2PA manifest injection** — support `ReplaceC2paManifestPath` option (model exists, implementation TODO) to replace stripped C2PA with a clean manifest
- [ ] planned — **Stripping IPTC/maker notes config keys** — expose `strip_iptc` and `strip_maker_notes` in `config.yaml` (model supports them, config doesn't surface them)
- [ ] planned — **PDF form-field metadata** — strip AcroForm / XFA metadata from PDFs
- [ ] planned — **PDF embedded files** — strip embedded file attachments and their metadata from PDFs

---

## P2 — Platform & UX (v1.x)

### CLI experience
- [ ] planned — `watermarkremover --version` command (read version from assembly metadata)
- [ ] planned — `clean-all` command — process a mixed directory: auto-detect file type and route to the appropriate cleaner (text/markdown → text pipeline, images → image pipeline, documents → metadata pipeline)
- [ ] planned — `batch` command — process a JSON/CSV manifest file with list of inputs + desired outputs (for automated pipelines)
- [ ] planned — `--quiet` / `-q` global option (suppress all output except errors; useful for scripting)
- [ ] planned — `--no-color` global option (disable Spectre ANSI; auto-detect non-TTY)
- [ ] planned — Exit codes documentation (`0` success, `1` input error, `2` detections found, `3` unsupported format, `4` model missing)
- [ ] planned — Shell completion scripts generation (`watermarkremover completions --shell powershell|bash|zsh|fish`)

### HTTP API enhancements
- [ ] planned — `POST /clean/markdown` endpoint (documented in README but missing in `ServeCommand.MapEndpoints` — currently only `clean/text`, `detect/text`, `clean/markdown` exists; add `detect/markdown`)
- [ ] planned — `POST /detect/markdown` endpoint
- [ ] planned — OpenAPI / Swagger UI at `/swagger` (via Swashbuckle) for API discoverability
- [ ] planned — CORS support (configurable allowed origins via `--cors-origins`)
- [ ] planned — Configurable rate-limit via `config.yaml` (currently hardcoded 100 req/min)
- [ ] planned — File size limit enforcement (configurable `max_upload_mb`, default 100 MB)
- [ ] planned — `/metrics` endpoint (Prometheus: request count, latency histogram, model availability)
- [ ] planned — Web UI — simple static HTML page at `/` with drag-and-drop file upload (vanilla JS, no framework dependency)

### Configuration
- [ ] planned — Environment variable overrides (`WATERMARKREMOVER__TEXT__STATISTICAL=true`, double-underscore notation like ASP.NET config)
- [ ] planned — Full markdown config surface — expose all 21 `MarkdownCleanOptions` toggles in `config.yaml` (only 12 are currently surfaced)
- [ ] planned — Config validation — fail fast with clear error on unknown keys / invalid values

---

## P3 — Quality & reliability (ongoing)

### Test coverage
- [ ] planned — `WatermarkRemover.CLI.Tests` project — test CLI commands (`CleanTextCommand`, `CleanMarkdownCommand`, `CleanFileCommand`, `CleanImageCommand`, `DetectTextCommand`, etc.) with `WebApplicationFactory` for HTTP endpoints
- [ ] planned — Integration tests — end-to-end: create temp files with known watermarks → run CLI → assert cleaned output
- [ ] planned — `WatermarkRemover.Core.Tests` project — test `ConfigLoader`, `AppConfig.Default`, `ErrorResult`
- [ ] planned — Property-based tests (FsCheck) for Unicode hygiene — random insertion of invisible chars into arbitrary text, assert all removed
- [ ] planned — Snapshot tests for markdown cleaner — verify output across a corpus of AI-generated markdown samples
- [ ] planned — Test coverage threshold enforcement in CI (e.g., `coverlet` threshold ≥ 80% line coverage)

### Code quality
- [ ] planned — Benchmark project (`WatermarkRemover.Benchmarks` with BenchmarkDotNet) — measure throughput of text cleaning, metadata stripping, image inpainting; track regressions in CI
- [ ] planned — Fix CA1822 (`ProcessCodeSegment` can be static), CA1826 (`Enumerable.First()` on indexable), CA1848/CA1873 (use `LoggerMessage` source generators), CA1711 (`SynonymDictionary` naming), CA1707 (test method underscores), CA1816 (`Dispose` missing `GC.SuppressFinalize`)
- [ ] planned — `Microsoft.Extensions.Logging` `LoggerMessage` source generator for high-performance logging in Image pipeline
- [ ] planned — XML doc comments on all public APIs (enable `GenerateDocumentationFile=true` on packable projects; fix all `CS1591` warnings)

### Reliability
- [ ] planned — Structured error responses in HTTP API (problem+json RFC 9457) instead of ad-hoc `ErrorResult`
- [ ] planned — Request timeout in HTTP API (configurable, default 30s per request)
- [ ] planned — Graceful shutdown for `serve` (SIGTERM → drain in-flight requests → close)
- [ ] planned — Health check endpoint deep mode (`/health?deep=true` → check model availability, disk space, memory)
- [ ] planned — Resilience in LLM back-translation: Polly retry policy (3 retries, exponential backoff) for transient HTTP failures

---

## P4 — Ecosystem & reach (post-1.0)

### Packaging & distribution
- [ ] planned — Homebrew tap (`brew install techbuzzz/tap/watermarkremover`) — formula referencing GitHub Release binaries
- [ ] planned — Scoop bucket (`scoop install watermarkremover`) — Windows package manager manifest
- [ ] planned — APT/YUM packages for Linux distributions
- [ ] planned — Winget manifest (`winget install WatermarkRemover`)
- [ ] planned — Static Linux binary (musl, `linux-musl-x64` RID) for maximum distro compatibility
- [ ] planned — NuGet packages published to nuget.org — `WatermarkRemover.Core`, `WatermarkRemover.Text`, `WatermarkRemover.Metadata`, `WatermarkRemover.Image` as consumable libraries

### Integrations
- [ ] planned — Python bindings — `pyo3` or `ctypes` wrapper exposing the text-cleaning pipeline as a Python package (`pip install watermarkremover`)
- [ ] planned — Node.js bindings — `napi-rs` wrapper for JS/TS consumers
- [ ] planned — Browser extension — context-menu "Clean AI watermarks" for selected text in Chrome/Firefox (calls local `serve` API)
- [ ] planned — VS Code extension — right-click "Clean watermarks" on selected text or files
- [ ] planned — GitHub Action — `uses: techbuzzz/ai-watermark-remover@v1` as a reusable workflow step for CI pipelines
- [ ] planned — Obsidian plugin — clean AI watermarks from markdown notes

### Research & advanced detection
- [ ] planned — **Token-level watermark scoring** — implement full Kirchenbauer detection (compute per-token logprob entropy, flag green-list bias via G-test) for text scored by LLMs with exposed logprobs (requires API integration with OpenAI/Anthropic logprob endpoints)
- [ ] planned — **SynthID-Text detector** — research and implement detection of Google's SynthID-Text statistical watermark (published algorithm: embedding-bias + sampling-bias)
- [ ] planned — **C2PA manifest reader** — full JUMBF/C2PA box parser to extract and display provenance claims before stripping
- [ ] planned — **Image forensics** — ELA (Error Level Analysis) detection of AI-generated image regions
- [ ] planned — **Audio watermarking** — research and implement removal of audio watermarks (if/when AI audio watermarks become standardized)
- [ ] planned — **Video metadata** — extend to MP4/MOV container metadata and C2PA manifests for video

---

## P5 — Long-term vision

- [ ] planned — **Real-time API gateway** — managed cloud service (serverless) with auth, rate limiting, billing
- [ ] planned — **Model marketplace** — community-contributed watermark detection models (ONNX) downloadable via `download-model --list`
- [ ] planned — **CLI plugin system** — load custom `IFileMetadataCleaner` / `IAiTextWatermarkDetector` implementations from DLLs at runtime
- [ ] planned — **Web dashboard** — full React/Blazor WASM frontend with batch processing, history, and settings
- [ ] planned — **Telemetry opt-in** — anonymous usage stats (which layers used, file types processed) to guide development priorities
- [ ] planned — **Multilingual CLI** — localize CLI help strings (Russian, German, Chinese, Japanese)