# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **How to read this file:**
> - **Added** — new features.
> - **Changed** — changes in existing functionality.
> - **Deprecated** — soon-to-be-removed features.
> - **Removed** — now-removed features.
> - **Fixed** — bug fixes.
> - **Security** — vulnerability fixes (also see [SECURITY.md](./SECURITY.md)).

---

## [Unreleased]

### Added
- **`POST /detect/markdown` endpoint** — new HTTP route in
  `ServeEndpointMapper.cs` (consumed by `serve`) that takes the same
  `{ markdown, stripAll }` body as `POST /clean/markdown` and returns
  `AiArtifact[]` — the same detector output the `detect-markdown` CLI
  command prints (frontmatter, AI signature, boilerplate disclaimer,
  invisible box-drawing separators, invisible characters inside code
  blocks). `stripAll` is intentionally ignored: detection uses a fixed
  detector set, not the cleaning toggles. 400 on empty body, 401 when
  `--api-key` is set and `X-API-Key` is missing (consistent with the
  rest of the API). 4 new tests in `HttpEndpointTests` cover the happy
  path (frontmatter + AI signature both reported), 400 on empty body,
  401 when auth is required, and that the new route is listed in
  `/swagger/v1/swagger.json`. README HTTP API table updated.
- **Full markdown config surface** — every public toggle on
  `MarkdownCleanOptions` is now reachable from `config.yaml`. The
  previous 12-key surface grew to all 21 (`strip_bold_italic`,
  `strip_blockquotes`, `strip_hr`, `strip_comments`, `strip_task_lists`,
  `strip_table_syntax`, `normalize_lists`, `unwrap_empty_lists`,
  `strip_xml_tags`, `apply_unicode_layer_a`); the legacy
  `preserve_code_blocks` knob is kept for backward compatibility. New
  `MarkdownCleanOptions.From(MarkdownConfig)` static factory in
  `WatermarkRemover.Core.Models` is the single source of truth for the
  binding — `clean-markdown`, `clean-all`, and the HTTP
  `POST /clean/markdown` endpoint all consume it. `clean-markdown` now
  reads the full config baseline; CLI flags (`--strip-all`,
  `--strip-code-fences`, `--strip-links`) override on a per-key basis.
  `docs/CONFIGURATION.md` documents every key with its default and
  one-line description. 30 new tests in `MarkdownConfigTests` and
  `ConfigYamlMarkdownTests` cover: every public boolean on
  `MarkdownCleanOptions` is surfaced in `MarkdownConfig`, the
  `MarkdownConfig` defaults equal the `MarkdownCleanOptions` defaults
  (so deleting a key from `config.yaml` is a no-op), `From()` round-trips
  every toggle, `--strip-all` enables every toggle, and a smoke test
  loads `src/config.yaml` and asserts all 21 keys are present.
- **Shell completion scripts** — new `completions --shell <bash|zsh|powershell|fish>`
  CLI command emits a static completion script for the requested shell,
  covering every sub-command and a curated set of common flags per
  command. Install with
  `watermarkremover completions --shell bash | sudo tee /etc/bash_completion.d/watermarkremover > /dev/null`
  (bash), drop the zsh script into `site-functions/_watermarkremover`,
  append the PowerShell block to `$PROFILE.CurrentUserAllHosts`, or
  redirect the fish script into `~/.config/fish/completions/`. See
  [`docs/SHELL-COMPLETION.md`](./docs/SHELL-COMPLETION.md) for the
  full install matrix. Static generator lives in
  `ShellCompletionScripts`; command list and option hints are
  kept in one place so all four shells stay in lockstep. 17 new
  tests in `CompletionsCommandTests` cover: every shell renders a
  script that names the main sub-commands, the bash script registers
  `complete -F _watermarkremover`, the zsh script starts with
  `#compdef watermarkremover`, the PowerShell script uses
  `Register-ArgumentCompleter`, the fish script uses the
  `complete` builtin, an unknown shell throws
  `ArgumentException`, and the command itself returns 1 on an
  unknown / empty shell and 0 with a valid script on stdout.
- **HTTP endpoint tests for `serve`** — the eight endpoints
  (`/health`, `POST /clean/text`, `POST /clean/markdown`,
  `POST /clean/file`, `POST /clean/image`, `POST /detect/text`,
  `POST /detect/image`, `POST /inspect/file`) plus the Swagger UI, the
  OpenAPI JSON, and the `wwwroot/` static-UI mount are now exercised
  end-to-end via `Microsoft.AspNetCore.TestHost`
  (`Microsoft.AspNetCore.Mvc.Testing 10.0.11`). 11 new tests in
  `HttpEndpointTests` cover: `/health` returns 200 + `{"status":"ok"}`;
  `/clean/text` strips U+200B (Layer A); `/clean/text` returns 400
  (`INVALID_INPUT`) on empty body; `/clean/text` returns 401 when
  `--api-key` is set and `X-API-Key` is missing; `/clean/text` returns
  200 with the right key; `/health` stays open even with auth on;
  `/swagger/index.html` returns the Swashbuckle UI HTML;
  `/swagger/v1/swagger.json` returns the OpenAPI 3.0 spec; `GET /`
  returns 200 with the bundled UI when `wwwroot/index.html` is shipped;
  `GET /` returns 404 when `--no-ui` is set; `GET /` returns 404 when
  `wwwroot/` is absent. The eight endpoints, the Swagger mount, and
  the static-UI mount were extracted out of `ServeCommand` into a new
  public static `ServeEndpointMapper` (with `MapEndpoints`,
  `MountSwagger`, and `MountStaticUi` helpers) so the test host can
  spin them up in-memory without binding Kestrel. `TextRequest` and
  `MarkdownRequest` are now top-level types in
  `WatermarkRemover.CLI.Commands` so the mapper (and tests) can
  reference them. 138 tests total, all green; 0 build warnings.
- **`clean-all` CLI command** — new `watermarkremover clean-all <path>`
  dispatches a single file or directory to the right pipeline per file:
  `.md`/`.markdown` → markdown cleaner, router-supported extensions
  (JPEG/PNG/PDF/DOCX/HTML/WebP) → metadata cleaner, `.txt`/`.text`/`.log`
  and no-extension files → text pipeline (Layers A/B/C). Unknown binary
  extensions (`.bin`, `.exe`, `.mp4`, …) are skipped with a warning
  rather than being fed to the text pipeline by mistake. Supports
  `--recursive` and `--dry-run`; honours the global `--json` flag. New
  `CleanAllClassifier` (pure routing decision) +
  `CleanAllCommand`; new `WatermarkRemover.CLI.Tests` project (the
  foundation for WR-S4) with 33 tests covering the classifier (markdown
  / router / text / no-extension / binary / null guards / ordering) and
  the full command (happy path on a mixed dir, recursive flag, dry-run,
  single-file mode, binary-skip, missing path). 127 tests total, all
  green.
- **File size limit enforcement** — multipart uploads to `/clean/file`,
  `/clean/image`, `/inspect/file`, and `/detect/image` are now rejected
  with HTTP 413 (`PAYLOAD_TOO_LARGE`) *before* the body is streamed to
  disk when they exceed the configured size cap. New `server.max_upload_mb`
  key in `config.yaml` (default `100`); CLI override `serve --max-upload-mb
  <n>`. `0` disables the limit (local dev only). Kestrel's
  `MaxRequestBodySize` is lifted to the configured limit so the
  framework-level 413 doesn't fire first — the structured `ErrorResult`
  JSON is always returned. New `MaxUploadMBTests` (11 tests) + 2
  `AppConfigTests` additions.
- **Configurable HTTP rate-limit** — the per-IP fixed-window limiter that
  used to be hard-coded at 100 requests / minute in `ServeCommand` is now
  driven by the new `server.rate_limit` section in `config.yaml`
  (`permit_limit`, `window_seconds`, `queue_limit`). Two new CLI flags
  override individual values at start-up: `serve --rate-limit <n>` and
  `serve --rate-window <seconds>`. Resolution order: CLI flag >
  `config.yaml` > built-in default (100 / 60 / 0). The active values and
  the source of the resolution are printed at start-up. Invalid values
  (`<= 0`) cause `serve` to exit with status `1` before binding sockets.
- **OpenAPI / Swagger UI** — interactive API documentation at
  `/swagger/` and the machine-readable OpenAPI 3.0 spec at
  `/swagger/v1/swagger.json`. All 8 HTTP endpoints are documented with
  request/response schemas, and the `X-API-Key` requirement is declared
  in the security section. Generated by `Swashbuckle.AspNetCore 10.1.1`
  against the live endpoint map; `Microsoft.OpenApi` is pinned to
  `2.7.5` to avoid the GHSA-v5pm-xwqc-g5wc (CVE-2026-49451) DoS
  advisory that affects the 2.0.0-preview.11 → 2.7.4 line.
- **Out-of-the-box experience** — every install path now produces a working
  API **and** web UI on the same port without extra steps:
  - The release pipeline (`.github/workflows/release.yml`) builds the Astro
    web UI in a new Node 22 step before `dotnet publish`, and the publish
    uses `IncludeAllContentForSelfExtract=true` so the static bundle is
    embedded in the single-file binary.
  - A new `Makefile` (Linux / macOS) and `scripts/build.ps1` (Windows
    PowerShell) wrap the whole flow in one command — `make build`,
    `make serve`, `make test`, or `scripts\build.ps1 -Serve`.
  - The Dockerfile `webbuild` stage (added with the UI itself) was already
    doing the same for Docker; the README and `docs/WEB-UI.md` now document
    it explicitly under "Out of the box".
- **Web UI (Astro "box")** — single-page plug-and-play dashboard at `/` with
  Text / Markdown / File / Image tabs. Built with Astro 5.x (`output: 'static'`,
  no UI framework, code-split per tab, <50 KB total page weight gzipped).
  Configured via two env vars (`PUBLIC_API_URL`, `PUBLIC_API_KEY`). Co-located
  with the .NET binary via `UseStaticFiles`; standalone deploys (Vercel /
  Netlify / GH Pages / nginx) also supported. See
  [`docs/WEB-UI.md`](./docs/WEB-UI.md).
- **`/web` Astro project** — `package.json`, `astro.config.mjs`, `tsconfig.json`,
  Tab components, vanilla-JS widgets, 20+ Vitest unit tests, `npm run build`
  that syncs `dist/` → `src/WatermarkRemover.CLI/wwwroot/`.
- **CORS middleware** in `ServeCommand` — `--cors-origins` flag +
  `WATERMARKREMOVER_CORS_ORIGINS` env var, smart defaults (`*` for open APIs,
  local dev hosts when `--api-key` is set).
- **`--no-ui` flag** in `ServeCommand` — skip serving `wwwroot/` for
  headless API-only deployments.
- **`OutputFormatter.Info`** — small additional method on the Spectre.Console
  helper for informational messages.
- **`docs/WEB-UI.md`** — comprehensive guide: dev loop, build, env vars,
  co-located serve, standalone deploy recipes, security notes, troubleshooting.
- **Multi-stage Docker build** — new `webbuild` stage (Node 22 alpine) that
  builds the Astro bundle and overlays it into the .NET source tree before
  `dotnet publish`.
- **`Makefile`** — `make`, `make build`, `make web`, `make dotnet`, `make test`,
  `make serve`, `make clean`, `make smoke` targets for Linux / macOS.
- **`scripts/build.ps1`** — one-command equivalent for Windows PowerShell.
  Handles npm deprecation warnings that otherwise trip `$ErrorActionPreference
  = 'Stop'`. Supports `-SkipWeb`, `-SkipDotnet`, `-Configuration`, `-Serve`,
  `-Port` switches.
- **Documentation overhaul** — comprehensive [README](./README.md) with badges, hero,
  TOC, install methods (binary / Docker / from source), comparison table, FAQ,
  Russian-language support section.
- **[CONTRIBUTING.md](./CONTRIBUTING.md)** — development environment, coding
  conventions, commit-message format (Conventional Commits), PR checklist,
  guides for adding new vendor detectors and metadata cleaners.
- **[SECURITY.md](./SECURITY.md)** — responsible-disclosure policy,
  supported-versions table, **responsible-use disclaimer**, threat model, HTTP
  server hardening notes.
- **[CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md)** — Contributor Covenant v2.1.
- **Issue templates** — `.github/ISSUE_TEMPLATE/bug-report.yml`,
  `feature-request.yml`, `config.yml`.
- **PR template** — `.github/PULL_REQUEST_TEMPLATE.md`.
- **[CODEOWNERS](./.github/CODEOWNERS)** — `@techbuzzz` owns all source paths.
- **[Dependabot config](./.github/dependabot.yml)** — weekly NuGet + GitHub
  Actions updates, with auto-merge for `patch` updates.
- **[FUNDING.yml](./.github/FUNDING.yml)** — funding links (GitHub Sponsors).
- **[docker-compose.yml](./docker-compose.yml)** — single-service dev compose
  with `./models` volume, optional API-key env var.
- **New docs** — [`docs/FAQ.md`](./docs/FAQ.md),
  [`docs/COMPARISON.md`](./docs/COMPARISON.md),
  [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md),
  [`docs/CONFIGURATION.md`](./docs/CONFIGURATION.md).
- **Russian-language synonyms** — extended built-in `SynonymDictionary`
  coverage (see `src/WatermarkRemover.Text/SynonymDictionary.cs`).

### Changed
- **Central package management (NuGet CPM)** — package versions are now
  declared in one place. New `src/Directory.Packages.props` lists every
  external package (Serilog, Spectre.Console, SixLabors.ImageSharp,
  OnnxRuntime, Swashbuckle, xunit, etc.); `Version="..."` is removed
  from every `<PackageReference>` in the 7 csprojs. Central management
  is activated via `<ManagePackageVersionsCentrally>true</
  ManagePackageVersionsCentrally>` in `Directory.Build.props`. Bumping
  a dependency now means editing one line. Verified: stray `Version`
  attributes correctly raise `NU1008`; build still 0 warnings; 70/70
  tests pass.
- README now uses badges, emoji-led section headings, and a TOC for faster
  navigation.

### Security
- Documented responsible-use policy, threat model, and HTTP-server hardening in
  [SECURITY.md](./SECURITY.md).

---

## [0.1.0] — 2026-08-21

> *Pre-release. The first tagged release will be cut from this state.*

### Added
- **Core pipeline** — `WatermarkRemover.Core` models, interfaces, configuration,
  DI contracts.
- **Text cleaning (three layers)** — Unicode hygiene, statistical green-list
  rewrite, vendor heuristics (Claude / Gemini / OpenAI).
- **Markdown cleaning** — 20+ toggleable transforms, fenced code-block
  preservation, AI-signature removal.
- **Metadata cleaners** — JPEG (segment parser), PNG (chunk filter),
  PDF (PdfPig rebuild), DOCX (OpenXML properties + revisions),
  HTML (meta + comments).
- **Image cleaning** — auto mask generation (alpha + colour-frequency
  heuristics) + LaMa ONNX inpainting; manual mask support; graceful
  degradation when the model is missing.
- **CLI** — Spectre.Console commands:
  `clean-text`, `clean-markdown`, `clean-file`, `clean-image`,
  `detect-text`, `detect-markdown`, `detect-watermark`, `inspect-file`,
  `download-model`, `serve`.
- **HTTP API** — ASP.NET Core Minimal API with `/health`, `POST /clean/text`,
  `/clean/markdown`, `/clean/file`, `/clean/image`, `/detect/text`,
  `/detect/image`, `/inspect/file`; rate-limited (100 req/min/IP); optional
  `X-API-Key` auth.
- **Configuration** — `src/config.yaml` with all layers and `Logging` knobs;
  resolution order documented.
- **Tests** — 62 tests across `WatermarkRemover.Text.Tests` (35),
  `WatermarkRemover.Metadata.Tests` (18), `WatermarkRemover.Image.Tests` (9).
- **CI** — `.github/workflows/build-and-test.yml` matrix on Windows + Linux
  with TRX + cobertura coverage artifacts.
- **Release pipeline** — `.github/workflows/release.yml` produces
  self-contained single-file binaries for `linux-x64`, `linux-arm64`,
  `win-x64`, `osx-x64` and attaches them to a GitHub Release.
- **Docker image** — multi-stage `Dockerfile` (alpine runtime, non-root user,
  `HEALTHCHECK`, `EXPOSE 5080`).
- **`global.json`** — pins the SDK to `10.0.400` (stable only).
- **`Directory.Build.props`** — common build conventions (warnings-as-errors,
  nullable, latest analyzers, `EnforceCodeStyleInBuild`).
- **Russian language support** — homoglyph-safe Unicode normalisation, built-in
  Russian synonym dictionary, dedicated unit tests in
  `StatisticalWatermarkRewriterTests` / `UnicodeHygieneCleanerTests` /
  `MarkdownCleanerTests` / `VendorDetectorTests`.

---

## Release history at a glance

| Tag       | Date       | Highlights                                                    |
|-----------|------------|---------------------------------------------------------------|
| (pending) | —          | First tagged release — see [Unreleased] above.                |

[Unreleased]: https://github.com/techbuzzz/ai-watermark-remover/compare/main...HEAD
[0.1.0]: https://github.com/techbuzzz/ai-watermark-remover/releases/tag/v0.1.0
