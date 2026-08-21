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
