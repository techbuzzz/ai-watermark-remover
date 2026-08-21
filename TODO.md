# TODO — WatermarkRemover

Active work queue. Each item below is **tick-ready**: a Mavis `worker` tick
can pick it up, read this file + the referenced files, and ship the work
without needing additional user input.

Status legend:
- `[ ]`  — **ready** (a tick can pick this up)
- `[~]`  — **in progress** (a tick is already on it)
- `[x]`  — **done** (merged to `main`)
- `[!]`  — **blocked** (waiting on external input / decision — see notes)

Each task has a stable ID: `WR-SNN` (sprint) or `WR-PNN` (backlog).
The same ID appears in [BACKLOG.md](./BACKLOG.md) so the tick worker can
cross-reference status. See [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)
for the module map and extension points.

---

## Ready for tick

Items are ordered by impact. A new tick should pick **the first `[ ]` item**
in this list.

### WR-S3. [ ] `clean-all` auto-routing command

- **Why:** BACKLOG P2 — let users point one command at a mixed directory
  and have the right pipeline (text / markdown / metadata / image) chosen
  per file by extension + magic bytes.
- **Scope:** `src/WatermarkRemover.CLI/Commands/`
- **Files to touch:**
  - New `CleanAllCommand.cs` — AsyncCommand that takes one positional
    path arg + `--recursive` flag. Walks the file tree, classifies each
    file via `IFileCleanerRouter.IsSupported(...)` (for documents/images)
    or extension `.md/.markdown` (markdown), falls back to
    `ITextCleaningPipeline` for `.txt/.text/anything-else` (text mode)
  - `src/WatermarkRemover.CLI/Program.cs` — `cfg.AddCommand<CleanAllCommand>(...)`
  - README "Commands" table — add `clean-all`
- **Acceptance:**
  - `clean-all ./mixed-dir` with a directory containing `a.txt`, `b.jpg`,
    `c.md`, `d.pdf` produces cleaned files for all 4 (or writes a summary
    to stdout)
  - `clean-all ./mixed-dir --dry-run` lists what would be cleaned and
    exits 0 without writing
  - At least 3 tests — happy path, recursive flag, unsupported-file handling
- **Risks:** Don't double-process. Be careful with text fallback
  (don't send binary files to the text pipeline). Reject `.png`/`.jpg`
  as text.
- **Backlog ref:** WR-P211

### WR-S4. [ ] `WatermarkRemover.CLI.Tests` project (WebApplicationFactory for HTTP)

- **Why:** BACKLOG P3 — the .NET test suite is 94 tests but **zero** cover
  the HTTP API or any command wiring. A regression in `ServeCommand`
  (e.g. CORS) is only caught manually.
- **Scope:** `src/tests/`
- **Files to touch:**
  - New `src/tests/WatermarkRemover.CLI.Tests/WatermarkRemover.CLI.Tests.csproj`
    — references `Microsoft.AspNetCore.Mvc.Testing`, mirrors existing
    test-project conventions
  - `WatermarkRemover.sln` — `dotnet sln add` the new project
  - `src/WatermarkRemover.CLI/Commands/ServeCommand.cs` — extract
    `MapEndpoints` (currently `private void`) into an internal
    `public static class ServeEndpointMapper` so tests can call it
    without spinning up Kestrel, OR use `WebApplicationFactory<Program>`
    to host in-memory
  - Add tests:
    - `GET /health` returns 200 + `{"status":"ok"}`
    - `POST /clean/text` happy path with ZWSP input
    - `POST /clean/text` 400 on empty body
    - `POST /clean/text` 401 when `--api-key` is set and `X-API-Key`
      header is missing
    - `GET /swagger` returns 200
    - `GET /` returns 200 with HTML when `wwwroot/index.html` is shipped;
      404 when `--no-ui` or `wwwroot/` is absent
- **Acceptance:**
  - `dotnet test` shows 94 + ~10 new tests, all green
  - The new project uses the same `xunit`/`FluentAssertions` style as the
    existing test projects (read one of them first)
- **Risks:** `WebApplicationFactory<Program>` requires `Program` to be
  partial or to expose an `IHost` builder. Currently `Program.cs` is a
  top-level `class Program` with `Main` — should still work, but if not,
  extract the host setup into a static method on `Program`.
- **Backlog ref:** WR-P311

### WR-S6. [ ] Shell completion scripts

- **Why:** BACKLOG P2 — operators scripting against `watermarkremover` have
  to type full option names; tab-completion is the standard expectation.
- **Scope:** `src/WatermarkRemover.CLI/`
- **Files to touch:**
  - New `src/WatermarkRemover.CLI/Commands/CompletionsCommand.cs` —
    `completions --shell powershell|bash|zsh|fish` emits the script
  - `src/WatermarkRemover.CLI/Program.cs` — register the command
  - README "Commands" table — add `completions`
  - `docs/INSTALLATION.md` (new) or a new `docs/SHELL-COMPLETION.md` —
    one-liner install instructions for each shell
- **Acceptance:**
  - `watermarkremover completions --shell bash > /etc/bash_completion.d/wr`
    works
  - `completions --shell powershell | Out-File $PROFILE.CurrentUserAllHosts`
    works
  - At least 1 test asserting the emitted script contains the command
    names (`clean-text`, `clean-markdown`, `clean-image`, `serve`, …)
- **Risks:** Spectre.Console.Cli has built-in completion support — read
  the Spectre docs first before hand-rolling; using the built-in path is
  much less work.
- **Backlog ref:** WR-P217

### WR-S7. [ ] Expose all 21 `MarkdownCleanOptions` toggles in `config.yaml`

- **Why:** BACKLOG P2 — `MarkdownCleanOptions` has 21 boolean flags; only
  ~12 are currently surfaced in `src/config.yaml`. Users can't disable
  the rest without recompiling.
- **Scope:** `src/config.yaml`, `src/WatermarkRemover.Core/Configuration/`,
  `docs/CONFIGURATION.md`
- **Files to touch:**
  - `src/config.yaml` — list every key from `MarkdownCleanOptions` (read
    the file first to see what's already there and what isn't)
  - `MarkdownCleaner` or wherever options get bound — ensure unknown keys
    are still tolerated (already the pattern via `ConfigLoader`)
  - `docs/CONFIGURATION.md` — document every key with one-line description
- **Acceptance:**
  - Every public boolean property on `MarkdownCleanOptions` has a
    corresponding key in `config.yaml` with a default value matching
    `MarkdownCleanOptions.Default`
  - A smoke test loads `src/config.yaml` and binds it to `AppConfig`
    without throwing on the new keys
- **Risks:** Default values must match the C# defaults or users will get
  a surprise behaviour change.
- **Backlog ref:** WR-P231

### WR-S8. [ ] `POST /detect/markdown` endpoint

- **Why:** BACKLOG P2 — README documents 8 endpoints but only 7 exist;
  `/detect/markdown` is missing. The C# detector exists; only the HTTP
  wiring is missing.
- **Scope:** `src/WatermarkRemover.CLI/Commands/ServeCommand.cs`
- **Files to touch:**
  - `ServeCommand.cs` — add `app.MapPost("/detect/markdown", ...)` that
    accepts `{ markdown }` and returns `IReadOnlyList<AiArtifact>`
  - README "HTTP API" table — add the row
  - 1 test — happy path
- **Acceptance:**
  - `curl -X POST http://localhost:5080/detect/markdown -d '{"markdown":"# hi\n"}'`
    returns 200 with a JSON array
  - `GET /swagger/v1/swagger.json` lists the new endpoint
- **Risks:** None — small, well-scoped. Done in 30 minutes once you read
  the existing `/detect/text` mapping as a template.
- **Backlog ref:** WR-P213

### WR-S9. [ ] Watermark version command (`--version`)

- **Why:** BACKLOG P2 — currently `--version` doesn't print the assembly
  version. Operators want to confirm what they're running.
- **Scope:** `src/WatermarkRemover.CLI/Program.cs`,
  `src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj`
- **Files to touch:**
  - `WatermarkRemover.CLI.csproj` — ensure the assembly version is set
    (e.g. `<Version>1.0.0</Version>` and `<InformationalVersion>`)
  - `Program.cs` — Spectre.Console.Cli has a built-in `--version` flag;
    if it's not picking up the version, wire `Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion`
    into a `VersionProvider` and register it
- **Acceptance:**
  - `watermarkremover --version` prints e.g. `watermarkremover 1.0.0`
  - `dotnet build` is clean
- **Risks:** Trivial.
- **Backlog ref:** WR-P2110

---

## In progress

*(empty — no tick is currently assigned)*

---

## Blocked

*(empty — no item is currently waiting on user input)*

---

## Recently done

These were completed in the most recent sprint; they live here for context
but have already been moved to BACKLOG.md `[x]` and CHANGELOG.md `[Unreleased]`.

- [x] **WR-S1 — Configurable HTTP rate-limit** — `server.rate_limit.{permit_limit,
      window_seconds, queue_limit}` in `config.yaml` (defaults 100/60/0),
      CLI overrides `--rate-limit` / `--rate-window` on `serve`. New
      `WatermarkRemover.Core.Tests` project with 13 tests covering
      `AppConfig.Default` shape, `RateLimitConfig` defaults, and the
      CLI/config merge. Verified end-to-end: `serve --rate-limit 3
      --rate-window 30` returns 200 for the first 3 requests and 429
      for the 4th+. Invalid values fail fast with exit code 1.
- [x] **WR-S2 — File size limit enforcement** — `server.max_upload_mb` in `config.yaml`
      (default 100 MB), CLI override `--max-upload-mb` on `serve`. A middleware
      rejects multipart uploads to `/clean/file`, `/clean/image`,
      `/inspect/file`, `/detect/image` with HTTP 413 + structured
      `ErrorResult("PAYLOAD_TOO_LARGE", …)` *before* the body is streamed to
      disk. Kestrel's `MaxRequestBodySize` is lifted to the configured limit
      so the framework doesn't emit a bare 413 first. `0` disables the limit
      for local dev. New `MaxUploadMBTests` (11 tests) + 2 `AppConfigTests`
      additions — total 94 tests, all green. Verified end-to-end: 2 MB upload
      with `--max-upload-mb 1` → 413; `/health` still 200.
- [x] **WR-S5 — `Directory.Packages.props` for central package management** —
      new `src/Directory.Packages.props` listing 20 packages
      (Serilog, Spectre.Console, SixLabors.ImageSharp, OnnxRuntime,
      Swashbuckle, etc.). `Version="..."` removed from every
      `<PackageReference>` in all 7 csprojs. Central management
      activated via `<ManagePackageVersionsCentrally>true</
      ManagePackageVersionsCentrally>` in `Directory.Build.props`.
      Verified: NU1008 fires for stray `Version`; clean build still
      0 warnings; 94 tests pass.
- [x] **OpenAPI / Swagger UI at `/swagger`** — interactive UI at
      `/swagger` and OpenAPI 3.0 spec at `/swagger/v1/swagger.json` for all
      8 endpoints (text, markdown, file, image, health). Generated from the
      live endpoint map via Swashbuckle.AspNetCore 10.1.1; `X-API-Key` is
      declared in the security section. Pinned `Microsoft.OpenApi` to 2.7.5
      to avoid the GHSA-v5pm-xwqc-g5wc (CVE-2026-49451) DoS advisory.
- [x] **Web UI (Astro "box")** — single-page plug-and-play dashboard with
      Text / Markdown / File / Image tabs. See [`docs/WEB-UI.md`](./docs/WEB-UI.md).
      Includes: `/web` Astro project, CORS middleware in `ServeCommand`,
      `--cors-origins` + `WATERMARKREMOVER_CORS_ORIGINS`, `--no-ui` flag,
      Dockerfile `webbuild` stage, Vitest unit tests (20 passing),
      `scripts/build.ps1` + `Makefile` for one-command local builds,
      release.yml Node 22 step + `IncludeAllContentForSelfExtract=true`
      for embedded web UI in single-file release binaries.
- [x] **CORS support** in `ServeCommand` — `--cors-origins` flag +
      `WATERMARKREMOVER_CORS_ORIGINS` env var, smart defaults.
- [x] **`--no-ui` flag** — skip serving `wwwroot/` for headless API-only
      deployments.
- [x] **Release workflow** — single-file self-contained binaries for 4 RIDs,
      with embedded web UI in the bundle.
- [x] **Dockerfile** — multi-stage build, alpine runtime, non-root,
      `HEALTHCHECK`, `EXPOSE 5080`. UI is built into the image via the
      `webbuild` stage.

---

## How a tick works on an item

1. **Read the item.** Each entry has `Why`, `Scope`, `Files to touch`,
   `Acceptance`, and `Risks`. That's the spec.
2. **Read referenced files first.** Especially
   [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) and the file the
   item names.
3. **Make a `work/<short-name>` branch** and implement.
4. **Run `dotnet build` + `dotnet test` + `npm test` (if web changes) +
   `npm run typecheck` (if web changes) + `npm run build` (if web changes).**
   All must be green.
5. **Open a PR** referencing the TODO line. The PR template has the
   checklist.
6. **On merge:** update this file to move the item from `Ready` to
   `Recently done` (in the same commit) and tick the matching BACKLOG.md
   line (same WR-ID, also in the same commit). Keep `[Unreleased]` CHANGELOG
   entry accurate.