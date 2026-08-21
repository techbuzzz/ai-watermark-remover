# TODO — WatermarkRemover

Active work queue. Each item below is **tick-ready**: a Mavis `worker` tick
can pick it up, read this file + the referenced files, and ship the work
without needing additional user input. Items move to BACKLOG.md when they
out-grow the current sprint, and to "Recently done" once merged.

Status legend:
- `[ ]`  — **ready** (a tick can pick this up)
- `[~]`  — **in progress** (a tick is already on it)
- `[x]`  — **done** (merged to `main`)
- `[!]`  — **blocked** (waiting on external input / decision — see notes)

Reference: [BACKLOG.md](./BACKLOG.md) for the long-term roadmap (P0–P5).
See [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) for the module map and
extension points the tick should read first.

---

## Ready for tick

Items are ordered by impact. A new tick should pick **the first `[ ]` item**
in this list unless it already covers one of the lower ones.

### 1. OpenAPI / Swagger UI at `/swagger`

- **Why:** API discoverability. The README already documents 8 endpoints but
  they have no machine-readable schema; a tick can generate it.
- **Scope:** `src/WatermarkRemover.CLI/`
- **Files to touch:**
  - `src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj` — add
    `Swashbuckle.AspNetCore` package reference
  - `src/WatermarkRemover.CLI/Commands/ServeCommand.cs` — add
    `builder.Services.AddEndpointsApiExplorer()`, `AddSwaggerGen()`, and
    `app.UseSwagger() + app.UseSwaggerUI()` after `MapEndpoints`. Mount at
    `/swagger` (also at `/swagger/v1/swagger.json`)
  - `src/tests/` (new) — optional smoke test that `/swagger/v1/swagger.json`
    returns 200 and contains the strings `cleanText`, `cleanMarkdown`,
    `cleanFile`, `inspectFile`, `cleanImage`, `detectImage`
- **Acceptance:**
  - `dotnet run --project src/WatermarkRemover.CLI -- serve` → `GET /swagger`
    returns the HTML UI; `GET /swagger/v1/swagger.json` returns JSON listing
    all 8 endpoints with request/response schemas
  - `dotnet build` and `dotnet test` still 0 warnings / 0 errors / green
  - Document the new endpoint in `docs/WEB-UI.md` "Endpoints" list and the
    README "HTTP API" table
- **Risks:** Swashbuckle 6.x is for .NET 9+; ensure 6.5+ for .NET 10. If
  schema generation has trouble with the multipart endpoints, use
  `[Consumes("multipart/form-data")]` and `[RequestSizeLimit]` attributes.

### 2. Configurable rate-limit via `config.yaml`

- **Why:** Currently `ServeCommand.cs:58-71` hard-codes `PermitLimit = 100`
  and `Window = TimeSpan.FromMinutes(1)`. Operators want to tune this
  without recompiling.
- **Scope:** `src/WatermarkRemover.CLI/Commands/ServeCommand.cs`,
  `src/WatermarkRemover.Core/Configuration/AppConfig.cs`,
  `src/config.yaml`, `docs/CONFIGURATION.md`
- **Files to touch:**
  - `AppConfig.cs` — add `Server.RateLimit { PermitLimit, WindowSeconds,
    QueueLimit }` with `Default` values matching today's hard-coded 100/60/0
  - `src/config.yaml` — add a `server:` section with the same keys
  - `ServeCommand.cs` — read from `_config.Server.RateLimit` instead of
    literals; **also** accept CLI overrides `--rate-limit <int>` and
    `--rate-window <seconds>` for the headless case
  - `docs/CONFIGURATION.md` — document the new section
- **Acceptance:**
  - `RateLimitOptions` tests in `WatermarkRemover.Core.Tests` (if it
    exists) or extend `AppConfigTests` — assert YAML → typed config
    binding, default fallback, and unknown-key tolerance
  - `serve --rate-limit 5 --rate-window 10` actually limits to 5 req / 10s
    (test with `curl` in a loop; expect 6th request to return 429)
  - Resolution order documented: CLI > `config.yaml` > built-in default
- **Risks:** Keep `GlobalSettings` precedence clean. Don't break the existing
  rate limiter behavior when the new keys are absent.

### 3. File size limit enforcement (server-side, `max_upload_mb`)

- **Why:** `ServeCommand.cs` accepts any-size multipart uploads. The web UI
  already pre-checks 100 MB on the client (see
  `web/src/components/widgets/file-widget.ts` and
  `web/src/components/widgets/image-widget.ts` `MAX_BYTES`). A malicious
  client can bypass the check.
- **Scope:** `src/WatermarkRemover.CLI/`
- **Files to touch:**
  - `AppConfig.cs` — add `Server.MaxUploadMB` (default 100)
  - `ServeCommand.cs` — add `[RequestSizeLimit(long)]` attribute on
    `/clean/file` and `/clean/image`, or wrap them with a small middleware
    that reads `Content-Length` and returns `413 Payload Too Large` before
    streaming. Prefer middleware so the rejection happens before the file
    is copied to disk
  - `config.yaml` — expose `server.max_upload_mb: 100`
  - `docs/CONFIGURATION.md` and `docs/WEB-UI.md` — document
  - Optionally: `--max-upload-mb` CLI flag for the headless case
- **Acceptance:**
  - Upload a 200 MB file → server returns 413 with a JSON body matching the
    existing `ErrorResult` shape (`{ "code": "PayloadTooLarge", "message": "..." }`)
  - Upload a 5 MB file → still works
  - Setting `max_upload_mb: 10` in `config.yaml` and uploading 15 MB → 413
- **Risks:** Kestrel's default request body size is 30 MB. Use
  `KestrelServerOptions.Limits.MaxRequestBodySize` or the per-endpoint
  attribute. The middleware approach is cleaner because it gives a proper
  ErrorResult response, not just a generic Kestrel 413.

### 4. `clean-all` auto-routing command

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
  - Backlog tick for the matching BACKLOG P2 line
- **Acceptance:**
  - `clean-all ./mixed-dir` with a directory containing `a.txt`, `b.jpg`,
    `c.md`, `d.pdf` produces cleaned files for all 4 (or writes a summary
    to stdout)
  - `clean-all ./mixed-dir --dry-run` lists what would be cleaned and
    exits 0 without writing
  - At least 3 tests in `WatermarkRemover.CLI.Tests` (or extension of
    existing tests) — happy path, recursive flag, unsupported-file
    handling
- **Risks:** Don't double-process. Be careful with text fallback
  (don't send binary files to the text pipeline). Reject `.png`/`.jpg`
  as text.

### 5. `WatermarkRemover.CLI.Tests` project (WebApplicationFactory for HTTP)

- **Why:** BACKLOG P3 — currently the .NET test suite is 70 tests but
  **zero** cover the HTTP API or any command wiring. A regression in
  `ServeCommand` (e.g. CORS) is only caught manually.
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
    - `GET /swagger` (after task 1 lands) returns 200
    - `GET /` returns 200 with HTML when `wwwroot/index.html` is shipped;
      404 when `--no-ui` or `wwwroot/` is absent
- **Acceptance:**
  - `dotnet test` shows 70 + ~10 new tests, all green
  - The new project uses the same `xunit`/`FluentAssertions` style as the
    existing test projects (read one of them first)
- **Risks:** `WebApplicationFactory<Program>` requires `Program` to be
  partial or to expose an `IHost` builder. Currently `Program.cs` is a
  top-level `class Program` with `Main` — should still work, but if not,
  extract the host setup into a static method on `Program`.

### 6. `Directory.Packages.props` for central package management

- **Why:** BACKLOG P0 — today, `WatermarkRemover.CLI.csproj` pins
  `Serilog 4.0.0` and `Spectre.Console 0.49.0`; the other csprojs each
  have their own versions. Bumping requires touching many files.
- **Scope:** All `.csproj` files + new `src/Directory.Packages.props`
- **Files to touch:**
  - New `src/Directory.Packages.props` — central `<PackageVersion>` for
    every package currently in any csproj
  - Each `src/**/*.csproj` — change `<PackageReference Include="X"
    Version="..." />` to `<PackageReference Include="X" />` (the
    version lives in `Directory.Packages.props`)
  - `src/WatermarkRemover.sln` — usually no change needed
- **Acceptance:**
  - `dotnet restore` succeeds
  - `dotnet build` still 0 warnings / 0 errors
  - `dotnet test` still 70/70 green
  - Only one place to bump a version
- **Risks:** C# SDK has to be .NET 8+ for `Directory.Packages.props` to
  be supported (we're on .NET 10, fine). The feature requires
  `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
  in `Directory.Build.props` (which already exists at repo root — add the
  property there if missing).

### 7. Shell completion scripts

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

### 8. Expose all 21 `MarkdownCleanOptions` toggles in `config.yaml`

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

### 9. `POST /detect/markdown` endpoint

- **Why:** BACKLOG P2 — README documents 8 endpoints but only 7 exist;
  `/detect/markdown` is missing. The C# `IDetectMarkdownCommand` /
  detector exists; only the HTTP wiring is missing.
- **Scope:** `src/WatermarkRemover.CLI/Commands/ServeCommand.cs`
- **Files to touch:**
  - `ServeCommand.cs` — add `app.MapPost("/detect/markdown", ...)` that
    accepts `{ markdown }` and returns `IReadOnlyList<MarkdownArtifact>`
  - README "HTTP API" table — add the row
  - 1 test in `WatermarkRemover.CLI.Tests` (or wherever the new test
    project from item 5 lands) — happy path
- **Acceptance:**
  - `curl -X POST http://localhost:5080/detect/markdown -d '{"markdown":"# hi\n"}'`
    returns 200 with a JSON array
  - `GET /swagger/v1/swagger.json` (after item 1) lists the new endpoint
- **Risks:** None — small, well-scoped. Done in 30 minutes once you read
  the existing `/detect/text` mapping as a template.

### 10. Watermark version command (`--version`)

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
  - `watermarkremover --version` prints e.g. `watermarkremover 1.0.0 (commit abc1234)`
  - `dotnet build` is clean
- **Risks:** Trivial.

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
   line (also in the same commit). Keep `[Unreleased]` CHANGELOG entry
   accurate.
