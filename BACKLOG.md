# Backlog — WatermarkRemover

Prioritized list of improvements, features, and infrastructure work to make
WatermarkRemover the most popular AI-watermark-removal tool.

Status legend:
- `[ ]`  — **pending** (not started)
- `[~]`  — **in-progress** (currently being worked on by a tick)
- `[x]`  — **done** (completed and committed)

---

## P0 — Release readiness (must-have before v1.0)

### CI/CD & automation
- [x] GitHub Actions workflow: `build-and-test.yml` (restore, build, test on push/PR; matrix: windows + ubuntu; `dotnet test --logger trx --collect:"XPlat Code Coverage"`; upload coverage to codecov)
- [x] GitHub Actions workflow: `release.yml` (on tag `v*` — `dotnet publish -c Release -r {linux-x64,win-x64,osx-x64,linux-arm64}` self-contained, zip artifacts, create GitHub Release with attached binaries)
- [x] `global.json` pinning the SDK version (`10.0.400`) for reproducible builds
- [ ] `Directory.Packages.props` for central package management (single version source for all NuGet refs)
- [ ] Dependabot config (`.github/dependabot.yml`) for NuGet + GitHub Actions updates

### Containerization
- [x] `Dockerfile` (multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` build → `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` runtime, non-root user, `EXPOSE 5080`, `HEALTHCHECK CMD curl /health`)
- [x] `.dockerignore` (exclude `bin/`, `obj/`, `.vs/`, `models/`, `logs/`, `*.user`)
- [ ] `docker-compose.yml` (single-service dev compose; volume for models, env for `--api-key`)

### Distribution
- [x] `dotnet publish` publish profiles for self-contained single-file executables (`PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier`, `EnableCompressionInSingleFile=true`) — `src/WatermarkRemover.CLI/Properties/PublishProfiles/{linux-x64,linux-arm64,win-x64,osx-x64}.pubxml`
- [ ] NuGet packaging: mark `WatermarkRemover.Core` / `.Text` / `.Metadata` / `.Image` as `IsPackable=true`, set `PackageId` / `PackageVersion`, `PackageReadmeFile`, `PackageIcon`, create `.snk` for strong naming
- [ ] `dotnet tool` packaging for a potential global tool install (`dotnet tool install -g watermarkremover`)

### Docs
- [x] `CONTRIBUTING.md` (build/test instructions, code style, PR process, commit conventions)
- [x] `SECURITY.md` (responsible disclosure for a tool that removes tracking watermarks)
- [x] `CHANGELOG.md` (Keep a Changelog format)
- [x] GitHub issue templates: `.github/ISSUE_TEMPLATE/bug-report.yml` + `feature-request.yml`
- [x] `.github/PULL_REQUEST_TEMPLATE.md`
- [x] `.github/CODEOWNERS`
- [x] `CODE_OF_CONDUCT.md` (Contributor Covenant)
- [x] `.github/FUNDING.yml` (GitHub Sponsors)
- [x] `.github/dependabot.yml` (NuGet + GitHub Actions + Docker)
- [x] `docker-compose.yml` (single-service dev compose; volume for models, env for `--api-key`)
- [x] README overhaul: badges, hero, install methods, comparison, FAQ link, TOC
- [x] New docs: `docs/FAQ.md`, `docs/COMPARISON.md`, `docs/ARCHITECTURE.md`, `docs/CONFIGURATION.md`

---

## P1 — Core features (v1.0)

### New file format support
- [ ] **TIFF** metadata cleaner (`TIFFMetadataCleaner`) — strip EXIF, IPTC, XMP, ICC; preserve pixel data via `SixLabors.ImageSharp`
- [ ] **HEIF/HEIC** metadata cleaner — strip EXIF/XMP from Apple's modern photo format
- [x] **WebP** metadata cleaner — byte-level chunk parser for VP8/VP8L/VP8X RIFF (strip EXIF, XMP, ICC chunks)
- [ ] **AVIF** metadata cleaner — ISOBMFF box parser for EXIF/XMP
- [ ] **EPUB** metadata cleaner — strip OPF metadata (creator, contributor, dc:identifier, etc.) via zip-rewrite
- [ ] **PPTX/XLSX** metadata cleaners — extend OpenXML approach beyond DOCX
- [ ] **RTF** metadata cleaner — strip `\author`, `\generator`, `\doccomm` control words
- [ ] **MP4/MOV** metadata cleaner — strip `moov/udta/©xyz` atom (GPS), `udta/meta/keys/ilst` (title/author)

### Text layer enhancements
- [ ] **DeepSeek vendor detector** — heuristic patterns for DeepSeek text watermarks
- [ ] **Grok/xAI vendor detector** — detect Grok-specific artifacts
- [ ] **Mistral vendor detector** — detect Mistral-specific artifacts
- [ ] **Expand synonym dictionary** — increase EN coverage from ~140 → 400+ headwords; RU from ~50 → 200+
- [ ] **Configurable synonym dictionary** — load custom synonyms from `config.yaml` or an external JSON file
- [ ] **Additional language synonym sets** — German, French, Spanish, Chinese, Japanese
- [ ] **Layer B: LLM-based paraphrase mode** — full sentence rewrite (not just token swap) via Ollama endpoint with configurable temperature / top-p
- [ ] **Token watermark detection** — implement Kirchenbauer green-list entropy analysis (chi-square test on logprob distribution) for text scored by LLMs that expose logprobs

### Image pipeline enhancements
- [ ] **Additional inpainting backends** — plug-in support for MAT (Mask-Aware Transformer), LaMa-Plus, MI-GAN ONNX models
- [ ] **Semantic mask via ONNX segmentation** — U2Net or SAM-based watermark detection (detect logos, text overlays by semantic segmentation, not just alpha/color heuristics)
- [ ] **Manual mask via coordinate spec** — `--mask-rect x,y,w,h` CLI option for precise manual watermark bounding boxes
- [ ] **Batch image processing** — `clean-image ./images/ --recursive` support (mirror `clean-file` behaviour)
- [ ] **GPU inference** — ONNX Runtime CUDA/MemoryExecutionProvider auto-detect and use when available
- [ ] **Tiled inference** — split large images into overlapping 512×512 tiles for models that require fixed resolution, then stitch

### Metadata cleaner enhancements
- [ ] **C2PA manifest injection** — support `ReplaceC2paManifestPath` option (model exists, implementation TODO) to replace stripped C2PA with a clean manifest
- [ ] **Stripping IPTC/maker notes config keys** — expose `strip_iptc` and `strip_maker_notes` in `config.yaml` (model supports them, config doesn't surface them)
- [ ] **PDF form-field metadata** — strip AcroForm / XFA metadata from PDFs
- [ ] **PDF embedded files** — strip embedded file attachments and their metadata from PDFs

---

## P2 — Platform & UX (v1.x)

### CLI experience
- [ ] `watermarkremover --version` command (read version from assembly metadata)
- [ ] `clean-all` command — process a mixed directory: auto-detect file type and route to the appropriate cleaner (text/markdown → text pipeline, images → image pipeline, documents → metadata pipeline)
- [ ] `batch` command — process a JSON/CSV manifest file with list of inputs + desired outputs (for automated pipelines)
- [ ] `--quiet` / `-q` global option (suppress all output except errors; useful for scripting)
- [ ] `--no-color` global option (disable Spectre ANSI; auto-detect non-TTY)
- [ ] Exit codes documentation (`0` success, `1` input error, `2` detections found, `3` unsupported format, `4` model missing)
- [ ] Shell completion scripts generation (`watermarkremover completions --shell powershell|bash|zsh|fish`)

### HTTP API enhancements
- [ ] `POST /clean/markdown` endpoint (documented in README but missing in `ServeCommand.MapEndpoints` — currently only `clean/text`, `detect/text`, `clean/markdown` exists; add `detect/markdown`)
- [ ] `POST /detect/markdown` endpoint
- [ ] OpenAPI / Swagger UI at `/swagger` (via Swashbuckle) for API discoverability
- [ ] CORS support (configurable allowed origins via `--cors-origins`)
- [ ] Configurable rate-limit via `config.yaml` (currently hardcoded 100 req/min)
- [ ] File size limit enforcement (configurable `max_upload_mb`, default 100 MB)
- [ ] `/metrics` endpoint (Prometheus: request count, latency histogram, model availability)
- [ ] Web UI — simple static HTML page at `/` with drag-and-drop file upload (vanilla JS, no framework dependency)

### Configuration
- [ ] Environment variable overrides (`WATERMARKREMOVER__TEXT__STATISTICAL=true`, double-underscore notation like ASP.NET config)
- [ ] Full markdown config surface — expose all 21 `MarkdownCleanOptions` toggles in `config.yaml` (only 12 are currently surfaced)
- [ ] Config validation — fail fast with clear error on unknown keys / invalid values

---

## P3 — Quality & reliability (ongoing)

### Test coverage
- [ ] `WatermarkRemover.CLI.Tests` project — test CLI commands (`CleanTextCommand`, `CleanMarkdownCommand`, `CleanFileCommand`, `CleanImageCommand`, `DetectTextCommand`, etc.) with `WebApplicationFactory` for HTTP endpoints
- [ ] Integration tests — end-to-end: create temp files with known watermarks → run CLI → assert cleaned output
- [ ] `WatermarkRemover.Core.Tests` project — test `ConfigLoader`, `AppConfig.Default`, `ErrorResult`
- [ ] Property-based tests (FsCheck) for Unicode hygiene — random insertion of invisible chars into arbitrary text, assert all removed
- [ ] Snapshot tests for markdown cleaner — verify output across a corpus of AI-generated markdown samples
- [ ] Test coverage threshold enforcement in CI (e.g., `coverlet` threshold ≥ 80% line coverage)

### Code quality
- [ ] Benchmark project (`WatermarkRemover.Benchmarks` with BenchmarkDotNet) — measure throughput of text cleaning, metadata stripping, image inpainting; track regressions in CI
- [ ] Fix CA1822 (`ProcessCodeSegment` can be static), CA1826 (`Enumerable.First()` on indexable), CA1848/CA1873 (use `LoggerMessage` source generators), CA1711 (`SynonymDictionary` naming), CA1707 (test method underscores), CA1816 (`Dispose` missing `GC.SuppressFinalize`)
- [ ] `Microsoft.Extensions.Logging` `LoggerMessage` source generator for high-performance logging in Image pipeline
- [ ] XML doc comments on all public APIs (enable `GenerateDocumentationFile=true` on packable projects; fix all `CS1591` warnings)

### Reliability
- [ ] Structured error responses in HTTP API (problem+json RFC 9457) instead of ad-hoc `ErrorResult`
- [ ] Request timeout in HTTP API (configurable, default 30s per request)
- [ ] Graceful shutdown for `serve` (SIGTERM → drain in-flight requests → close)
- [ ] Health check endpoint deep mode (`/health?deep=true` → check model availability, disk space, memory)
- [ ] Resilience in LLM back-translation: Polly retry policy (3 retries, exponential backoff) for transient HTTP failures

---

## P4 — Ecosystem & reach (post-1.0)

### Packaging & distribution
- [ ] Homebrew tap (`brew install techbuzzz/tap/watermarkremover`) — formula referencing GitHub Release binaries
- [ ] Scoop bucket (`scoop install watermarkremover`) — Windows package manager manifest
- [ ] APT/YUM packages for Linux distributions
- [ ] Winget manifest (`winget install WatermarkRemover`)
- [ ] Static Linux binary (musl, `linux-musl-x64` RID) for maximum distro compatibility
- [ ] NuGet packages published to nuget.org — `WatermarkRemover.Core`, `WatermarkRemover.Text`, `WatermarkRemover.Metadata`, `WatermarkRemover.Image` as consumable libraries

### Integrations
- [ ] Python bindings — `pyo3` or `ctypes` wrapper exposing the text-cleaning pipeline as a Python package (`pip install watermarkremover`)
- [ ] Node.js bindings — `napi-rs` wrapper for JS/TS consumers
- [ ] Browser extension — context-menu "Clean AI watermarks" for selected text in Chrome/Firefox (calls local `serve` API)
- [ ] VS Code extension — right-click "Clean watermarks" on selected text or files
- [ ] GitHub Action — `uses: techbuzzz/ai-watermark-remover@v1` as a reusable workflow step for CI pipelines
- [ ] Obsidian plugin — clean AI watermarks from markdown notes

### Research & advanced detection
- [ ] **Token-level watermark scoring** — implement full Kirchenbauer detection (compute per-token logprob entropy, flag green-list bias via G-test) for text scored by LLMs with exposed logprobs (requires API integration with OpenAI/Anthropic logprob endpoints)
- [ ] **SynthID-Text detector** — research and implement detection of Google's SynthID-Text statistical watermark (published algorithm: embedding-bias + sampling-bias)
- [ ] **C2PA manifest reader** — full JUMBF/C2PA box parser to extract and display provenance claims before stripping
- [ ] **Image forensics** — ELA (Error Level Analysis) detection of AI-generated image regions
- [ ] **Audio watermarking** — research and implement removal of audio watermarks (if/when AI audio watermarks become standardized)
- [ ] **Video metadata** — extend to MP4/MOV container metadata and C2PA manifests for video

---

## P5 — Long-term vision

- [ ] **Real-time API gateway** — managed cloud service (serverless) with auth, rate limiting, billing
- [ ] **Model marketplace** — community-contributed watermark detection models (ONNX) downloadable via `download-model --list`
- [ ] **CLI plugin system** — load custom `IFileMetadataCleaner` / `IAiTextWatermarkDetector` implementations from DLLs at runtime
- [ ] **Web dashboard** — full React/Blazor WASM frontend with batch processing, history, and settings
- [ ] **Telemetry opt-in** — anonymous usage stats (which layers used, file types processed) to guide development priorities
- [ ] **Multilingual CLI** — localize CLI help strings (Russian, German, Chinese, Japanese)
