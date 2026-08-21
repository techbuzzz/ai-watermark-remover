# AGENTS.md

## Build & test

- .NET 10 SDK pinned via `global.json` (`10.0.400`, `rollForward: latestPatch`).
- `dotnet build src/WatermarkRemover.sln` — 0 warnings, 0 errors (warnings-as-errors is global via `Directory.Build.props`).
- `dotnet test src/WatermarkRemover.sln --no-build` — 94 tests across 4 test projects. No ONNX model needed (image tests use `FakeInpaintRunner`).
- Single suite: `dotnet test src/WatermarkRemover.sln --filter "FullyQualifiedName~UnicodeHygiene"`.
- **Web UI** (Astro, optional): `cd web && npm install && npm run build` → syncs `web/dist/` into `src/WatermarkRemover.CLI/wwwroot/` via `scripts/sync.mjs`. Run `npm test` (Vitest) and `npm run typecheck` (`astro check`) if touching `web/`.
- One-command full build: `make build` (Linux/macOS) or `.\scripts\build.ps1` (Windows).
- **CPM**: all NuGet versions are centralized in `src/Directory.Packages.props`. Never put `Version=` on a `<PackageReference>` — it triggers `NU1008`.

## Architecture invariants

- `Core` is the leaf: models + interfaces only, no behaviour, no deps on siblings.
- `Text`, `Metadata`, `Image` are siblings — none references the others. Cross-cutting goes through `Core` interfaces.
- `CLI` (`Program.cs`) is the composition root: DI registration, Spectre.Console.Cli command wiring, ASP.NET Core `serve` host.
- `AssemblyName` is `watermarkremover` (not `WatermarkRemover.CLI`). `InvariantGlobalization=true` on the CLI project.

## Code style

- `.editorconfig` is the source of truth — file-scoped namespaces (warning), braces on new line, `_camelCase` private fields.
- `TreatWarningsAsErrors=true` globally; `EnableNETAnalyzers=true`; `AnalysisLevel=latest`; `EnforceCodeStyleInBuild=true`.
- Primary constructors, `record` for DTOs, `ILogger<T>` (never `Console.WriteLine` in library code), no `.Result`/`.Wait()`/`async void`.

## HTTP API (`serve`)

- `dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080`.
- Swagger UI at `/swagger`, spec at `/swagger/v1/swagger.json`.
- Config: `src/config.yaml` (snake_case → `AppConfig` via `YamlDotNet`). Resolution: `--config` → `./config.yaml` → exe dir → defaults. Unknown keys ignored.
- Rate-limit + upload-size configurable: `server.rate_limit.*`, `server.max_upload_mb`, CLI flags `--rate-limit`, `--rate-window`, `--max-upload-mb`.

## Docker

- Multi-stage: `node:22-alpine` (webbuild) → `dotnet/sdk:10.0` (build) → `aspnet:10.0-alpine` (runtime, non-root `wr` user).
- `docker build -t watermarkremover .` → `docker run -p 5080:5080 watermarkremover`.
- Publishes `linux-musl-x64` (Alpine musl), framework-dependent.

## Task tracking

- `TODO.md` — tick-ready sprint items (`WR-SNN` IDs, `[ ]` ready / `[~]` in-progress / `[x]` done).
- `BACKLOG.md` — full roadmap (`WR-PNNN` IDs, P0–P6). `docs/BACKLOG.md` is retired (redirect only).
- Tick worker: pick first `[ ]` in TODO.md, mark `[~]` in both files, implement, mark `[x]` in both, commit.

## Commit convention

- Conventional Commits: `<type>(<scope>): <subject>`. Scopes: `text`, `metadata`, `image`, `cli`, `http`, `core`, `docs`, `docker`, `deps`, `config`.
- Never amend, force-push, or rebase. Releases are manual: tag `vX.Y.Z` → triggers `release.yml`.

## Key files

- `src/WatermarkRemover.CLI/Program.cs` — entry point, DI, command registration.
- `src/WatermarkRemover.CLI/Commands/ServeCommand.cs` — HTTP API + Swagger + CORS + rate-limit + upload-size middleware.
- `src/WatermarkRemover.Core/Configuration/AppConfig.cs` — typed config mirror of `src/config.yaml`.
- `src/Directory.Packages.props` — central NuGet versions.
- `Directory.Build.props` — global build settings + metadata.
- `docs/ARCHITECTURE.md` — module map and extension points.
