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
- [x] WR-P2110 — `watermarkremover --version` command (→ TODO WR-S9)
- [x] WR-P211 — `clean-all` command (→ TODO WR-S3)
- [ ] WR-P212 — `batch` command — process a JSON/CSV manifest file with list of inputs + desired outputs (for automated pipelines)
- [x] WR-P213 — `POST /detect/markdown` endpoint (→ TODO WR-S8)
- [ ] WR-P214 — `--quiet` / `-q` global option (suppress all output except errors; useful for scripting)
- [ ] WR-P215 — `--no-color` global option (disable Spectre ANSI; auto-detect non-TTY)
- [ ] WR-P216 — Exit codes documentation (`0` success, `1` input error, `2` detections found, `3` unsupported format, `4` model missing`)
- [x] WR-P217 — Shell completion scripts generation (→ TODO WR-S6)

### HTTP API enhancements
- [x] WR-P221 — `POST /clean/markdown` endpoint — exists; `ServeCommand.cs:148-158` already implements it. Closed.
- [x] WR-P222 — `POST /detect/markdown` endpoint (→ TODO WR-S8, same as WR-P213)
- [x] WR-P223 — OpenAPI / Swagger UI at `/swagger` — completed 2026-08-21; spec at `/swagger/v1/swagger.json`, interactive UI at `/swagger/`
- [x] WR-P224 — CORS support (configurable allowed origins via `--cors-origins`)
- [x] WR-P225 — Configurable rate-limit via `config.yaml` (→ TODO WR-S1)
- [x] WR-P226 — File size limit enforcement (configurable `max_upload_mb`, default 100 MB) (→ TODO WR-S2)
- [ ] WR-P227 — `/metrics` endpoint (Prometheus: request count, latency histogram, model availability)
- [x] WR-P228 — Web UI (Astro "box") — single-page plug-and-play dashboard at `/` with Text / Markdown / File / Image tabs. Astro 5.x static output, no UI framework, code-split per tab. Co-located with the .NET binary via `UseStaticFiles` (single-file releases embed the bundle via `IncludeAllContentForSelfExtract`). Standalone deploys (Vercel / Netlify / GH Pages / nginx) also supported. See [`docs/WEB-UI.md`](./docs/WEB-UI.md).

### Configuration
- [ ] WR-P231 — Environment variable overrides (`WATERMARKREMOVER__TEXT__STATISTICAL=true`, double-underscore notation like ASP.NET config)
- [x] WR-P232 — Full markdown config surface — expose all 21 `MarkdownCleanOptions` toggles in `config.yaml` (only 12 are currently surfaced) (→ TODO WR-S7)
- [ ] WR-P233 — Config validation — fail fast with clear error on unknown keys / invalid values

---

## P3 — Quality & reliability (ongoing)

### Test coverage
- [x] WR-P311 — `WatermarkRemover.CLI.Tests` project — test CLI commands with `WebApplicationFactory` for HTTP endpoints (→ TODO WR-S4)
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

---

## P6 — Agent integration (MCP, skills, plugins)

The goal of this phase is to make WatermarkRemover a **first-class tool
inside AI coding assistants and agent runtimes**, not just a standalone
CLI / HTTP service. Three pillars:

1. **MCP server** — expose the full pipeline as Model Context Protocol
   tools so any MCP-compatible agent (Claude Code, OpenCode, MiniMax Code,
   Cursor, Continue, etc.) can call `clean_text`, `clean_markdown`,
   `clean_file`, `clean_image`, `detect_text`, `inspect_file` directly
   without shelling out to the CLI.
2. **Agent skills** — drop-in skill packages (`SKILL.md` + scripts) that
   teach agents *when* and *how* to use the tool, installable via a single
   copy/clone into the agent's skills directory.
3. **IDE/agent plugins** — native extensions for the most popular agent
   hosts (OpenCode, MiniMax Code, Claude Code) that wire the tool into the
   editor context menu, slash commands, and agentic workflows.

### MCP server
- [x] WR-P601 — **MCP server core** — new `WatermarkRemover.Mcp` project exposing the pipeline as MCP tools. Uses the official `ModelContextProtocol` C# SDK (NuGet: `ModelContextProtocol` for stdio, `ModelContextProtocol.AspNetCore` for HTTP). Tools are defined with `[McpServerToolType]` + `[McpServerTool]` attribute-based discovery — each tool is a static method that calls the existing pipeline interface (`ITextCleaningPipeline`, `IMarkdownCleaner`, `IFileCleanerRouter`, `IImageCleaningPipeline`); no new business logic. Tools: `clean_text`, `clean_markdown`, `clean_file`, `clean_image`, `detect_text`, `detect_markdown`, `inspect_file`, `detect_watermark`. Registered in DI via `AddMcpServer().WithToolsFromAssembly()`. All logs go to **stderr** (stdout is the JSON-RPC channel for stdio transport).
- [x] WR-P602 — **MCP `serve-mcp` CLI command** — new `serve-mcp` command in `WatermarkRemover.CLI`. Uses `Host.CreateApplicationBuilder()` + `AddMcpServer().WithStdioServerTransport()` for local agent integration (default). For HTTP/SSE transport, uses `WebApplication.CreateBuilder()` + `AddMcpServer().WithHttpTransport(o => o.Stateless = true)` + `app.MapMcp()` (reuses the existing ASP.NET Core host from `serve`). `--transport stdio|http` flag. Registered alongside the existing `serve` command in `Program.cs`.
- [x] WR-P603 — **MCP server config** — add `mcp:` section to `config.yaml` (`transport`, `port`, `api_key`, `rate_limit`) and `McpConfig` to `AppConfig.cs`. Defaults: stdio transport, port 5090, no auth (local agent), inherits `server.rate_limit` when not set.
- [x] WR-P604 — **MCP server tests** — `WatermarkRemover.Mcp.Tests` project. Uses the SDK's `StreamServerTransport` / `StreamClientTransport` (in-memory pipe transport) to test the full JSON-RPC handshake (`initialize` → `tools/list` → `tools/call`) without spawning a subprocess. Each of the 8 tools verified with a known input. Error mapping: invalid input → `CallToolResult` with `IsError = true` (tool errors are distinct from protocol errors per the MCP spec). At least 10 tests.
- [x] WR-P605 — **MCP server docs** — new `docs/MCP.md` with: architecture diagram, tool schemas (request/response JSON for all 8 tools), transport options (stdio / Streamable HTTP / legacy SSE), configuration reference, and install instructions for Claude Code (`claude mcp add`), OpenCode, MiniMax Code, Cursor (`~/.cursor/mcp.json`), Continue. Linked from README.

### Agent skills (drop-in `SKILL.md` + scripts)
- [~] WR-P611 — **Skill: `watermark-clean-text`** — `skills/clean-text/SKILL.md` teaching agents: "when the user pastes AI-generated text or asks to remove watermarks / invisible characters, call the `clean_text` MCP tool or pipe through `watermarkremover clean-text`". Includes a `run.sh` / `run.ps1` wrapper script and examples for EN + RU text. Installable by copying the folder into the agent's skills directory.
- [~] WR-P612 — **Skill: `watermark-clean-markdown`** — `skills/clean-markdown/SKILL.md` for markdown-specific cleanup: strip AI signatures, frontmatter, invisible chars inside code blocks, but preserve code structure. Includes before/after examples.
- [~] WR-P613 — **Skill: `watermark-clean-file`** — `skills/clean-file/SKILL.md` for metadata stripping: "when the user uploads a JPEG/PNG/PDF/DOCX/HTML/WebP file, call `clean_file` or `inspect_file` to strip EXIF/XMP/C2PA before sharing". Includes a file-type → cleaner mapping table.
- [~] WR-P614 — **Skill: `watermark-clean-image`** — `skills/clean-image/SKILL.md` for visual watermark removal: "when the user asks to remove a logo/watermark from an image, call `detect_watermark` first, then `clean_image`". Includes mask guidance and LaMa model setup instructions.
- [~] WR-P615 — **Skill: `watermark-detect`** — `skills/detect/SKILL.md` for detection-only workflows: "when the user wants to *check* if text has AI watermarks without modifying it, call `detect_text` / `detect_markdown`". Includes interpretation guidance for the `WatermarkMatch[]` result.
- [~] WR-P616 — **Skills installer** — `skills/install.ps1` + `skills/install.sh` script that copies the relevant skill folders into the target agent's skills directory (auto-detects Claude Code `~/.claude/skills/`, OpenCode `.opencode/skills/`, generic `./skills/`). `--agent claude|opencode|minimax|generic` flag. Documented in `docs/SKILLS.md`.
- [~] WR-P617 — **Skills repo / registry** — publish the `skills/` directory as a standalone installable unit (git submodule, npm package, or just a `git clone` + `install.sh`). Versioned in lockstep with the CLI. Listed in the README "Installation" section.

### IDE / agent plugins
- [ ] WR-P621 — **OpenCode plugin** — `.opencode/plugin/watermark-remover/` with: `plugin.json` manifest, slash commands (`/wr-clean-text`, `/wr-clean-file`, `/wr-detect`), MCP server auto-start config, and a `SKILL.md` so the OpenCode agent learns the tool. Registered in the OpenCode plugin registry. Works with both local stdio MCP and the `serve` HTTP API.
- [ ] WR-P622 — **Claude Code integration** — `.claude/skills/watermark-remover/` with `SKILL.md` + `mcp-config.json` pointing at the stdio MCP server. Installable via `claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp`. Includes a `hooks.json` snippet for auto-cleaning pasted text. Documented in `docs/CLAUDE-CODE.md`.
- [ ] WR-P623 — **MiniMax Code integration** — `minimax-code/watermark-remover/` plugin with: `manifest.json`, MCP server registration, slash commands, and a skill file. Follows the MiniMax Code extension format. Documented in `docs/MINIMAX-CODE.md`.
- [ ] WR-P624 — **Cursor / Continue MCP config** — prebuilt `mcp-config.json` snippets for Cursor (`~/.cursor/mcp.json`) and Continue (`~/.continue/config.json`) that register the WatermarkRemover MCP server. No plugin code needed — just the config template + install instructions in `docs/MCP.md`.
- [ ] WR-P625 — **VS Code extension (MCP-based)** — lightweight VS Code extension that: (1) auto-starts the MCP server, (2) registers `cleanText` / `cleanFile` / `detectText` as VS Code commands, (3) adds a context-menu "Clean AI watermarks" on selected text and files, (4) ships the skills folder. Uses the `vscode-languageclient` + MCP transport. Published to the VS Code Marketplace.

### Packaging & distribution for agent integrations
- [ ] WR-P631 — **`watermarkremover serve-mcp` in release binaries** — ensure the `serve-mcp` command and MCP transport are included in the single-file self-contained release binaries. The `ModelContextProtocol` SDK is a pure managed NuGet library with no native deps, so `PublishSingleFile=true` + `SelfContained=true` works out of the box. Add `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` to `Directory.Packages.props`. Verified in the release workflow with a `serve-mcp --help` smoke test.
- [ ] WR-P632 — **npm package `@watermarkremover/mcp`** — thin Node.js wrapper that spawns the MCP server binary and exposes it as an npm-installable MCP server (`npx @watermarkremover/mcp`). For agents that prefer npm-based MCP registration. Includes `bin` entry, `package.json`, and a postinstall script that downloads the platform-appropriate binary from GitHub Releases.
- [ ] WR-P633 — **Docker image for MCP** — extend the Dockerfile to `EXPOSE 5090` for the MCP Streamable HTTP transport. `docker run -p 5090:5090 watermarkremover serve-mcp --transport http --port 5090` starts the MCP HTTP server. Documented in `docs/MCP.md`.