# TODO — WatermarkRemover

Current sprint work items. Move items to BACKLOG.md when done.
Reference: [BACKLOG.md](./BACKLOG.md) for full feature roadmap.

Status legend:
- `[ ]`  — **pending** (not started)
- `[~]`  — **in-progress** (currently being worked on by a tick)
- `[x]`  — **done** (completed and committed)

---

## Active — P0 release readiness

- [x] **Release workflow** — `.github/workflows/release.yml`
      Triggered on `v*` tag. Publish self-contained single-file binaries for
      `linux-x64`, `win-x64`, `osx-x64`, `linux-arm64`. Attach to GitHub Release.

- [x] **global.json** — pin SDK to `10.0.400` (rollForward `latestPatch`, stable only).

- [x] **Dockerfile** — multi-stage build, alpine runtime, non-root, `EXPOSE 5080`.
- [x] **.dockerignore** — exclude `bin/ obj/ .vs/ models/ logs/`.
- [x] **docker-compose.yml** — dev service with model volume + API-key env.

- [x] **Publish profiles** — self-contained single-file `dotnet publish` config (linux-x64, linux-arm64, win-x64, osx-x64).

- [x] **CONTRIBUTING.md** — build/test, code style, PR process, commit conventions.
- [x] **SECURITY.md** — responsible disclosure + responsible-use policy.
- [x] **CHANGELOG.md** — initial entries + Keep a Changelog format.
- [x] **Issue templates** — `.github/ISSUE_TEMPLATE/bug-report.yml`, `feature-request.yml`, `config.yml`.
- [x] **PR template** — `.github/PULL_REQUEST_TEMPLATE.md`.
- [x] **CODEOWNERS** — `.github/CODEOWNERS`.
- [x] **CODE_OF_CONDUCT.md** — Contributor Covenant v2.1.
- [x] **README overhaul** — badges, hero, TOC, install methods, comparison, FAQ, Russian.
- [x] **New docs** — `docs/FAQ.md`, `docs/COMPARISON.md`, `docs/ARCHITECTURE.md`, `docs/CONFIGURATION.md`.
- [x] **Dependabot** — `.github/dependabot.yml` (NuGet + GitHub Actions + Docker, weekly).
- [x] **FUNDING.yml** — `.github/FUNDING.yml` (GitHub Sponsors).

## Next — P1 features

- [x] WebP metadata cleaner (byte-level RIFF chunk parser)
- [ ] TIFF metadata cleaner (ImageSharp-based)
- [ ] Expand synonym dictionary (EN → 400+, RU → 200+)
- [ ] DeepSeek / Grok / Mistral vendor detectors
- [ ] Image batch processing (`clean-image ./images/ --recursive`)
- [ ] GPU inference (CUDA ExecutionProvider auto-detect)
- [ ] Configurable rate-limit via config.yaml
- [ ] OpenAPI / Swagger UI endpoint
- [ ] `clean-all` auto-routing command

## Tech debt — P3

- [ ] `WatermarkRemover.CLI.Tests` project (WebApplicationFactory for HTTP)
- [ ] Fix CA1822, CA1826, CA1848, CA1711, CA1707, CA1816 analyzer violations
- [ ] `LoggerMessage` source generators in Image pipeline
- [ ] XML doc comments on all public APIs (CS1591)
- [ ] `Directory.Packages.props` central package management
