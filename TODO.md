# TODO — WatermarkRemover

Current sprint work items. Move items to BACKLOG.md when done.
Reference: [BACKLOG.md](./BACKLOG.md) for full feature roadmap.

---

## Active — P0 release readiness

- [x] **Release workflow** — `.github/workflows/release.yml`
      Triggered on `v*` tag. Publish self-contained single-file binaries for
      `linux-x64`, `win-x64`, `osx-x64`, `linux-arm64`. Attach to GitHub Release.

- [x] done — **global.json** — pin SDK to `10.0.400` (rollForward `latestPatch`, stable only).

- [ ] **Dockerfile** — multi-stage build, alpine runtime, non-root, `EXPOSE 5080`.
- [ ] **.dockerignore** — exclude `bin/ obj/ .vs/ models/ logs/`.
- [ ] **docker-compose.yml** — dev service with model volume.

- [ ] **Publish profiles** — self-contained single-file `dotnet publish` config.

- [ ] **CONTRIBUTING.md** — build/test, code style, PR process.
- [ ] **SECURITY.md** — responsible disclosure.
- [ ] **CHANGELOG.md** — initial entry for current state.
- [ ] **Issue templates** — `.github/ISSUE_TEMPLATE/bug-report.yml`, `feature-request.yml`.
- [ ] **PR template** — `.github/PULL_REQUEST_TEMPLATE.md`.
- [ ] **CODEOWNERS** — `.github/CODEOWNERS`.
- [ ] **CODE_OF_CONDUCT.md** — Contributor Covenant.

## Next — P1 features

- [ ] WebP metadata cleaner (byte-level RIFF chunk parser)
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