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

**Sprint phases:**
- Items WR-S1..WR-S9 — current sprint (P0-P3 platform work)
- Items WR-S10..WR-S20 — **Phase 6: Agent integration** (MCP server,
  skills, OpenCode/Claude Code/MiniMax Code plugins, VS Code extension,
  npm package, Docker MCP). Pick in order - MCP server (WR-S10) must
  land before skills and plugins.

---

## Ready for tick

Items are ordered by impact. A new tick should pick **the first `[ ]` item**
in this list.

### WR-S3. [x] `clean-all` auto-routing command

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

### WR-S4. [x] `WatermarkRemover.CLI.Tests` project (WebApplicationFactory for HTTP)

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

### WR-S6. [x] Shell completion scripts

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

### WR-S7. [x] Expose all 21 `MarkdownCleanOptions` toggles in `config.yaml`

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

### WR-S8. [x] `POST /detect/markdown` endpoint

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

### WR-S9. [x] Watermark version command (`--version`)

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

## Phase 6 — Agent integration (MCP, skills, plugins)

These tasks build the agent-integration layer so WatermarkRemover works
inside Claude Code, OpenCode, MiniMax Code, Cursor, Continue, and any
MCP-compatible host. Full specs in [BACKLOG.md → P6](./BACKLOG.md#p6--agent-integration-mcp-skills-plugins).
Pick in order — MCP server must land before skills and plugins can use it.

### WR-S10. [x] MCP server core (`WatermarkRemover.Mcp` project)

- **Why:** WR-P601 — expose the full pipeline as MCP tools so any
  MCP-compatible agent can call `clean_text`, `clean_markdown`,
  `clean_file`, `clean_image`, `detect_text`, `detect_markdown`,
  `inspect_file`, `detect_watermark` directly — no shell-out needed.
- **SDK:** Official `ModelContextProtocol` C# SDK
  (https://github.com/modelcontextprotocol/csharp-sdk, NuGet
  `ModelContextProtocol` for stdio, `ModelContextProtocol.AspNetCore`
  for HTTP). Maintained in collaboration with Microsoft. Uses attribute
  based tool discovery: `[McpServerToolType]` on the class,
  `[McpServerTool]` on each method, `[Description]` on params — the SDK
  auto-generates the JSON Schema 2020-12 for each tool from the C# method
  signature. Transport via `WithStdioServerTransport()` (stdio) or
  `WithHttpTransport(o => o.Stateless = true)` + `app.MapMcp()` (HTTP).
- **Scope:** new `src/WatermarkRemover.Mcp/` project
- **Files to touch:**
  - New `src/WatermarkRemover.Mcp/WatermarkRemover.Mcp.csproj` —
    references `WatermarkRemover.Core`, `.Text`, `.Metadata`, `.Image`,
    and NuGet `ModelContextProtocol` (stdio) +
    `Microsoft.Extensions.Hosting`. Target `net10.0`.
  - New `src/WatermarkRemover.Mcp/Tools/CleanTextTool.cs` —
    `[McpServerToolType] public static class CleanTextTool` with a
    `[McpServerTool] public static async Task<string> CleanText(
    [Description("Text to clean")] string text,
    [Description("Enable Layer B statistical rewrite")] bool? statistical = null,
    CancellationToken ct = default)` method. The method calls
    `ITextCleaningPipeline.CleanAsync()` (injected via DI) and returns
    `result.Cleaned` as a string (auto-wrapped in `TextContentBlock` by
    the SDK). For richer output, return `IEnumerable<TextContentBlock>`
    including removed-items summary when `--verbose` is requested.
  - New `src/WatermarkRemover.Mcp/Tools/` — one file per tool:
    - `CleanMarkdownTool.cs` — calls `IMarkdownCleaner.Clean()`
    - `CleanFileTool.cs` — calls `IFileCleanerRouter.Clean()` (writes
      to a temp file, returns base64 `BlobResourceContents` for binary
      formats)
    - `CleanImageTool.cs` — calls `IImageCleaningPipeline.CleanAsync()`
      (returns cleaned image as `ImageContentBlock.FromBytes()`)
    - `DetectTextTool.cs` — calls `ITextCleaningPipeline.Detect()`
    - `DetectMarkdownTool.cs` — calls `IMarkdownCleaner.Detect()`
    - `InspectFileTool.cs` — calls `IFileCleanerRouter.Inspect()`
    - `DetectWatermarkTool.cs` — calls
      `IImageCleaningPipeline.Detect()` (returns JSON array of
      `DetectedRegion`)
  - New `src/WatermarkRemover.Mcp/DependencyInjection.cs` —
    `AddWatermarkRemoverMcp(this IServiceCollection services, AppConfig
    config)` extension method. Calls `AddMcpServer()`,
    `.WithStdioServerTransport()`, `.WithToolsFromAssembly()`.
  - `src/WatermarkRemover.sln` — `dotnet sln add` the new project
- **Logging:** All logging goes to **stderr** (`LogToStandardErrorThreshold
  = LogLevel.Trace`). stdout is reserved for the JSON-RPC protocol —
  never write to stdout from the server. Use `Host.CreateApplicationBuilder()`
  and configure `logging.AddConsole(o => o.LogToStandardErrorThreshold =
  LogLevel.Trace)`.
- **Acceptance:**
  - `dotnet build` clean, 0 warnings
  - stdio handshake works: send `initialize` → get server capabilities;
    send `tools/list` → get 8 tools with JSON Schema; send `tools/call`
    with `clean_text` + ZWSP input → get cleaned text
  - Each tool returns `TextContentBlock` (text tools), `ImageContentBlock`
    (image tool), or `EmbeddedResourceBlock` (file tool) as appropriate
  - At least 8 unit tests (one per tool) verifying the result shape
- **Risks:** The `ModelContextProtocol` NuGet package requires .NET 8+ —
  confirm it works on net10.0 (it should; the SDK targets `net8.0`+
  with `netstandard2.0` fallback). If `WithToolsFromAssembly()` doesn't
  discover tools in a separate project, use `.WithTools<CleanTextTool>()`
  with explicit type registration instead.
- **Backlog ref:** WR-P601

### WR-S11. [x] `serve-mcp` CLI command + MCP config

- **Why:** WR-P602, WR-P603 — add a `serve-mcp` command to the CLI so
  agents can start the MCP server, and add an `mcp:` section to
  `config.yaml`.
- **SDK:** The stdio transport uses `Host.CreateApplicationBuilder()` +
  `AddMcpServer().WithStdioServerTransport()` + `WithToolsFromAssembly()`.
  The HTTP transport uses `WebApplication.CreateBuilder()` +
  `AddMcpServer().WithHttpTransport(o => o.Stateless = true)` +
  `app.MapMcp()` (reuses the existing ASP.NET Core host pattern from
  `ServeCommand`). `MapMcp()` maps the Streamable HTTP endpoint at the
  root by default; use `app.MapMcp("/mcp")` for a custom route.
- **Scope:** `src/WatermarkRemover.CLI/`, `src/config.yaml`,
  `src/WatermarkRemover.Core/Configuration/`
- **Files to touch:**
  - New `src/WatermarkRemover.CLI/Commands/ServeMcpCommand.cs` —
    `AsyncCommand` that builds the host with the MCP transport:
    - **stdio** (default): `Host.CreateApplicationBuilder()` →
      `services.AddWatermarkRemoverMcp(config)` (which internally calls
      `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`)
      → `await host.RunAsync()`. Logging to stderr only.
    - **http**: `WebApplication.CreateBuilder()` →
      `AddMcpServer().WithHttpTransport(o => o.Stateless = true)`
      → `app.MapMcp()` → `await app.RunAsync()`. Reuses `--port`
      (default 5090), `--api-key` (optional, reuses the same
      auth middleware pattern from `ServeCommand`).
  - `src/WatermarkRemover.CLI/Program.cs` — register `serve-mcp`:
    `cfg.AddCommand<ServeMcpCommand>("serve-mcp").WithDescription(...)`
  - `src/WatermarkRemover.Core/Configuration/AppConfig.cs` — add
    `McpConfig { Transport, Port, ApiKey }` with defaults (stdio,
    5090, null)
  - `src/config.yaml` — add `mcp:` section
  - README "Commands" table — add `serve-mcp`
- **Acceptance:**
  - `dotnet run --project src/WatermarkRemover.CLI -- serve-mcp` starts
    the stdio MCP server and responds to JSON-RPC over stdin/stdout
  - `serve-mcp --transport http --port 5090` starts the Streamable HTTP
    transport at `http://0.0.0.0:5090` (testable with `curl`)
  - Config values overrideable via `config.yaml`
  - `--help` shows the new command with all flags
  - stdout contains ONLY JSON-RPC messages (no log output) in stdio mode
- **Risks:** stdio transport must not log to stdout (stdout is the
  JSON-RPC channel) — all logging goes to stderr via
  `LogToStandardErrorThreshold = LogLevel.Trace`. The HTTP transport
  reuses the existing `WatermarkRemover.CLI` project which already
  references `Microsoft.AspNetCore.App` framework, so
  `ModelContextProtocol.AspNetCore` can be added without a new
  framework reference.
- **Backlog ref:** WR-P602, WR-P603

### WR-S12. [x] MCP server tests

- **Why:** WR-P604 — test coverage for the MCP layer before building
  skills and plugins on top of it.
- **SDK:** Use `StreamServerTransport` / `StreamClientTransport`
  (in-memory pipe transport from the `ModelContextProtocol` SDK) to
  test the full JSON-RPC handshake without spawning a subprocess.
  Create paired pipes (`System.IO.Pipelines.Pipe`) and connect the
  server + client in the same process. This is the official testing
  pattern recommended by the SDK (see `samples/InMemoryTransport`).
- **Scope:** new `src/tests/WatermarkRemover.Mcp.Tests/`
- **Files to touch:**
  - New `src/tests/WatermarkRemover.Mcp.Tests/WatermarkRemover.Mcp.Tests.csproj`
    — references `ModelContextProtocol` (for the in-memory transport
    types), `WatermarkRemover.Mcp`, `WatermarkRemover.Core`, and the
    same test stack (`xunit`, `FluentAssertions`,
    `Microsoft.NET.Test.Sdk`) as the other test projects.
  - `src/WatermarkRemover.sln` — `dotnet sln add` the new project
  - Tests:
    - `Initialize_Handshake_ReturnsServerInfo` — send `initialize`
      JSON-RPC, assert `serverInfo` contains `"WatermarkRemover"`
    - `ToolsList_Returns8Tools` — send `tools/list`, assert 8 tool
      names match the expected set
    - `CleanText_RemovesZwsp` — `tools/call` `clean_text` with ZWSP
      input, assert cleaned text has no ZWSP
    - `CleanMarkdown_StripsFrontmatter` — `tools/call` `clean_markdown`
      with frontmatter, assert it's removed
    - `DetectText_FindsVendorWatermark` — `tools/call` `detect_text`
      with a known Claude/Gemini/OpenAI pattern, assert `WatermarkMatch[]`
    - `InspectFile_ReturnsMetadataEntries` — `tools/call`
      `inspect_file` with a test PNG containing tEXt chunk, assert
      `MetadataEntry[]`
    - `CleanFile_ReturnsBlobResource` — `tools/call` `clean_file` with
      a test PNG, assert `EmbeddedResourceBlock` with `BlobResourceContents`
    - `CleanImage_ReturnsImageContentBlock` — `tools/call`
      `clean_image` with a test image + fake inpaint runner, assert
      `ImageContentBlock` with `image/png` MIME
    - `DetectWatermark_ReturnsRegions` — `tools/call`
      `detect_watermark` with a test image, assert `DetectedRegion[]`
    - `EmptyInput_ReturnsToolError` — `tools/call` `clean_text` with
      empty string, assert `CallToolResult.IsError == true`
- **Acceptance:**
  - `dotnet test` shows all MCP tests green
  - At least 10 tests (8 tool tests + handshake + error case)
  - No ONNX model required (use `FakeInpaintRunner` from
    `WatermarkRemover.Image.Tests` or a test double)
- **Risks:** The in-memory transport requires `System.IO.Pipelines`
  (part of the .NET runtime). The `StreamServerTransport` constructor
  takes a `PipeReader` + `PipeWriter` — convert with
  `.AsStream()` if the API expects `Stream`.
- **Backlog ref:** WR-P604

### WR-S13. [x] MCP server docs (`docs/MCP.md`)

- **Why:** WR-P605 — document the MCP integration for agent developers
  and end users.
- **SDK ref:** Link to https://github.com/modelcontextprotocol/csharp-sdk
  and https://csharp.sdk.modelcontextprotocol.io/ as the SDK reference.
  Document the three transport modes (stdio, Streamable HTTP stateless,
  legacy SSE) and which one to use when.
- **Scope:** new `docs/MCP.md`
- **Files to touch:**
  - New `docs/MCP.md` — sections:
    - **Architecture** — diagram: `Agent → MCP transport (stdio/HTTP) →
      WatermarkRemover.Mcp → pipeline (text/markdown/file/image)`
    - **Tool schemas** — request/response JSON for all 8 tools
      (`clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
      `detect_text`, `detect_markdown`, `inspect_file`,
      `detect_watermark`). Each with parameter descriptions and example
      output (text → `TextContentBlock`, file → `EmbeddedResourceBlock`
      with `BlobResourceContents`, image → `ImageContentBlock`)
    - **Transports** — stdio (local agents, default), Streamable HTTP
      (remote, stateless recommended), legacy SSE (disabled by default,
      enable with `EnableLegacySse = true` for old clients)
    - **Configuration** — `mcp:` section in `config.yaml`, CLI flags
      (`--transport`, `--port`, `--api-key`)
    - **Install** — one-liner per host:
      - Claude Code: `claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp`
      - OpenCode: add to `.opencode/mcp-config.json`
      - MiniMax Code: add to plugin manifest
      - Cursor: `~/.cursor/mcp.json` snippet
      - Continue: `~/.continue/config.json` snippet
      - Docker: `docker run -p 5090:5090 watermarkremover serve-mcp --transport http`
  - README — add "Agent integration" section with link to `docs/MCP.md`
- **Acceptance:**
  - A user can follow `docs/MCP.md` to register the MCP server in any
    of the 5 listed hosts without reading source code
  - Every tool has a request/response example
  - SDK reference links are correct and live
- **Risks:** None — documentation only.
- **Backlog ref:** WR-P605

### WR-S14. [x] Agent skills (`skills/` directory + installer)

- **Why:** WR-P611..WR-P617 — drop-in skill packages that teach agents
  when and how to use WatermarkRemover. Installable by copying a folder.
- **Scope:** new `skills/` directory at repo root
- **Files to touch:**
  - `skills/clean-text/SKILL.md` — when to call `clean_text`, examples
    for EN + RU, wrapper script
  - `skills/clean-markdown/SKILL.md` — markdown-specific cleanup
  - `skills/clean-file/SKILL.md` — metadata stripping, file-type →
    cleaner mapping
  - `skills/clean-image/SKILL.md` — visual watermark removal, mask
    guidance
  - `skills/detect/SKILL.md` — detection-only workflow
  - `skills/install.ps1` + `skills/install.sh` — auto-detect agent
    skills dir (`--agent claude|opencode|minimax|generic`)
  - `docs/SKILLS.md` — skill reference and install instructions
  - README — add "Skills" section
- **Acceptance:**
  - `skills/install.sh --agent opencode` copies skills into
    `.opencode/skills/` and reports success
  - Each `SKILL.md` has: trigger conditions, tool call examples,
    error handling, and language notes (EN/RU)
  - At least 1 test verifying the installer script finds the correct
    target directory
- **Risks:** Skill format varies between agents — read each agent's
  skill spec first (OpenCode, Claude Code, MiniMax Code). Keep SKILL.md
  files generic enough to work across all three.
- **Backlog ref:** WR-P611..WR-P617

### WR-S15. [x] OpenCode plugin

- **Why:** WR-P621 — native OpenCode plugin with slash commands and MCP
  auto-start.
- **Scope:** new `.opencode/plugin/watermark-remover/`
- **Files to touch:**
  - `.opencode/plugin/watermark-remover/plugin.json` — manifest
  - `.opencode/plugin/watermark-remover/SKILL.md` — agent skill
  - `.opencode/plugin/watermark-remover/mcp-config.json` — MCP server
    auto-start config (stdio transport)
  - Slash commands: `/wr-clean-text`, `/wr-clean-file`, `/wr-detect`
  - `docs/MCP.md` — add OpenCode-specific install section
- **Acceptance:**
  - Plugin loads in OpenCode and slash commands appear
  - `/wr-clean-text "text with ZWSP"` returns cleaned text
  - MCP server auto-starts when the plugin is active
- **Risks:** Read the OpenCode plugin spec first — the format may differ
  from what's assumed here.
- **Backlog ref:** WR-P621

### WR-S16. [x] Claude Code integration

- **Why:** WR-P622 — Claude Code skill + MCP config for one-command
  setup.
- **Scope:** new `.claude/skills/watermark-remover/` + docs
- **Files to touch:**
  - `.claude/skills/watermark-remover/SKILL.md` — skill file
  - `.claude/skills/watermark-remover/mcp-config.json` — stdio MCP
    server config
  - `.claude/skills/watermark-remover/hooks.json` — auto-clean pasted
    text (optional)
  - `docs/CLAUDE-CODE.md` — install instructions:
    `claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp`
  - README — add "Claude Code" install line
- **Acceptance:**
  - `claude mcp list` shows `watermarkremover`
  - Claude Code can call `clean_text` on selected text
- **Risks:** Claude Code skill format may evolve — follow the latest
  Anthropic docs.
- **Backlog ref:** WR-P622

### WR-S17. [x] MiniMax Code integration

- **Why:** WR-P623 — MiniMax Code plugin with MCP registration.
- **Scope:** new `minimax-code/watermark-remover/`
- **Files to touch:**
  - `minimax-code/watermark-remover/manifest.json` — plugin manifest
  - `minimax-code/watermark-remover/SKILL.md` — skill file
  - `minimax-code/watermark-remover/mcp-config.json` — MCP server
    registration
  - `minimax-code/watermark-remover/commands/` — slash commands
  - `docs/MINIMAX-CODE.md` — install instructions
  - README — add "MiniMax Code" install line
- **Acceptance:**
  - Plugin loads in MiniMax Code and commands appear
  - MCP server connects and tools are callable
- **Risks:** MiniMax Code extension format may not be publicly
  documented — research first, document any assumptions.
- **Backlog ref:** WR-P623

### WR-S18. [ ] Cursor / Continue MCP config + npm package

- **Why:** WR-P624, WR-P632 — prebuilt MCP configs for Cursor and
  Continue, plus an npm wrapper for easy install.
- **Scope:** config templates + npm package
- **Files to touch:**
  - `docs/MCP.md` — add Cursor + Continue config snippets
  - New `npm/watermarkremover-mcp/` directory:
    - `package.json` (`name: "@watermarkremover/mcp"`, `bin` entry)
    - `index.js` — spawns the platform-appropriate binary from GitHub
      Releases and pipes stdio
    - `postinstall.js` — downloads the binary
  - README — add `npx @watermarkremover/mcp` install line
- **Acceptance:**
  - `npx @watermarkremover/mcp` starts the MCP server over stdio
  - Cursor config snippet from `docs/MCP.md` registers the server
  - Continue config snippet works
- **Risks:** npm package needs the release binaries published first —
  coordinate with the release workflow.
- **Backlog ref:** WR-P624, WR-P632

### WR-S19. [ ] VS Code extension (MCP-based)

- **Why:** WR-P625 — lightweight VS Code extension with context-menu
  "Clean AI watermarks" and slash commands.
- **Scope:** new `vscode/watermark-remover/` directory
- **Files to touch:**
  - `vscode/watermark-remover/package.json` — extension manifest
  - `vscode/watermark-remover/src/extension.ts` — activates MCP server,
    registers `cleanText` / `cleanFile` / `detectText` commands,
    adds context-menu items
  - `vscode/watermark-remover/skills/` — bundled skills
  - `vscode/watermark-remover/README.md` — marketplace listing
  - Build + publish to VS Code Marketplace
- **Acceptance:**
  - Extension installs and activates in VS Code
  - Right-click on selected text → "Clean AI watermarks" → cleaned text
  - Right-click on a file → "Strip metadata" → cleaned file
- **Risks:** VS Code extension API learning curve — use `yo code` to
  scaffold, keep the extension thin (delegate to MCP server).
- **Backlog ref:** WR-P625

### WR-S20. [ ] MCP packaging + Docker for MCP

- **Why:** WR-P631, WR-P633 — ensure `serve-mcp` is in release binaries
  and Docker exposes the MCP HTTP transport.
- **Scope:** release workflow, Dockerfile, Directory.Packages.props
- **Files to touch:**
  - `src/Directory.Packages.props` — add `ModelContextProtocol` and
    `ModelContextProtocol.AspNetCore` package versions (central package
    management)
  - `.github/workflows/release.yml` — verify `serve-mcp` command is
    included in single-file binaries (no extra runtime deps; the MCP
    SDK is a pure managed library). Add a smoke-test step that runs
    `./watermarkremover serve-mcp --help` on the built binary.
  - `Dockerfile` — add `EXPOSE 5090` for the MCP HTTP transport. Add a
    `CMD` variant for `serve-mcp --transport http --port 5090`.
    Alternatively, document in `docs/MCP.md` that users can run:
    `docker run -p 5090:5090 watermarkremover serve-mcp --transport http --port 5090`
  - `docker-compose.yml` — add MCP service or port mapping
  - `docs/MCP.md` — add Docker instructions
- **Acceptance:**
  - `docker run -p 5090:5090 watermarkremover serve-mcp --transport http`
    starts the MCP Streamable HTTP server at `http://0.0.0.0:5090`
  - Release binary includes `serve-mcp` (test on at least one RID by
    running `./watermarkremover serve-mcp --help`)
  - `ModelContextProtocol` packages listed in `Directory.Packages.props`
- **Risks:** None — packaging only. The MCP SDK is a pure managed
  library with no native dependencies, so single-file self-contained
  publish works out of the box.
- **Backlog ref:** WR-P631, WR-P633

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

- [x] **WR-S17 — MiniMax Code integration (`minimax-code/` + `docs/MINIMAX-CODE.md`)** —
      the project now ships a first-class MiniMax Code integration
      pre-wired into the repo as a V1 local plugin package at
      `minimax-code/watermark-remover/`: new
      `.minimax-plugin/plugin.json` (schemaVersion 1, name
      `watermark-remover`, category `Code`, 6 skills, 1 MCP
      server, 4 example queries, no apps, no `auth`/`oauth`/
      credentials), `servers.mcp.json` declaring the stdio MCP
      server `watermarkremover` (runs `watermarkremover serve-mcp`,
      30 s timeout, `env: {}`), a 128 KB `icon.png` (validated PNG
      signature, picked from the local MiniMax Code Code-category
      pool), six skills under `skills/` — master
      `watermark-remover/SKILL.md` (routing table + CLI fallback +
      worked examples, MiniMax-Code-specific — no slash-command
      auto-discovery section) plus the five per-format skills
      re-shipped from `skills/` (their `name` field matches their
      directory per the V1 spec, so the directory uses the longer
      `watermark-clean-text` shape, not the shorter `clean-text`),
      three forward-looking slash-command files under `commands/`
      (`wr-clean-text.md`, `wr-clean-file.md`, `wr-detect.md`) —
      the V1 manifest does not yet declare `commands` as a
      first-class capability, but the files are present for any
      future MiniMax Code version that auto-discovers
      `commands/*.md` the same way OpenCode does, and a
      plugin-level `README.md` documenting install + prerequisites
      + what-the-agent-sees layout. `docs/MCP.md → MiniMax Code`
      is rewritten to point at the new install method (drop the
      folder into the MiniMax Code plugin directory, toggle on,
      done) instead of the old hand-edited `mcp-config.json`
      recipe. New `docs/MINIMAX-CODE.md` is the end-to-end
      reference: package layout, install matrix (Linux / macOS /
      Windows + source-mode swap for `dotnet run`), verify-the-
      install walkthrough, slash-command status section, MCP
      transport notes (stdio default, Streamable HTTP swap,
      30 s timeout, stderr logging contract), full CLI fallback
      list, a 10-row troubleshooting table, and an "Assumptions
      and open questions" section calling out where the V1 spec
      is silent. README's `## 🧠 Agent skills` section gains a
      parallel "MiniMax Code users get a parallel pre-wired
      integration" callout with the OS-specific copy commands;
      the docs-link footer adds a `🧩 docs/MINIMAX-CODE.md` row.
      25 new tests in `MinimaxCodePluginManifestTests` cover the
      full V1 spec contract: manifest validity, all 10 required
      fields, schemaVersion == 1, name in kebab-case regex +
      directory-name match, version in SemVer, category in the
      10-value whitelist, description + every example query
      non-empty, `apps` is the empty array, at least one
      effective capability, no forbidden fields
      (`auth`/`oauth`/`client_id`/`client_secret`/`token`/
      `apiKey`/`api_key`), every `mcpServers` and `skills` path
      resolves, `icon` file exists and has the PNG signature,
      no path escapes, package fits within V1 limits (≤ 1024
      regular files, ≤ 2048 entries, ≤ 64 MiB total, ≤ 16 MiB
      per file), no reparse points / symlinks, README present,
      MCP config is valid JSON with non-empty `mcpServers` and
      supported transports, every skill's frontmatter `name`
      matches its directory with a non-empty `description`, and
      the three forward-looking slash-command files are present.
      Build clean (0 warnings, 0 errors), 346 tests total, all
      green (+25 in `WatermarkRemover.CLI.Tests`).
- [x] **WR-S16 — Claude Code integration (`.claude/` + `docs/CLAUDE-CODE.md`)** —
      the project now ships a first-class Claude Code integration
      pre-wired into the repo: new
      `.claude/skills/watermark-remover/SKILL.md` (master skill with
      routing table, CLI fallback, error contract — mirrors the
      OpenCode one but Claude-Code-specific since Claude invokes
      the MCP tools as ordinary tool calls rather than via
      auto-discovered slash-command files), drop-in
      `.claude/skills/watermark-remover/mcp-config.json` (project-
      level `mcpServers` snippet for `.mcp.json` or
      `.claude/settings.json` merge), optional
      `.claude/skills/watermark-remover/hooks.json` registering a
      `UserPromptSubmit` command hook that pipes the user's pasted
      text through `clean_text` and injects the cleaned version
      back as `hookSpecificOutput.additionalContext` (the
      `hooks/auto-clean.js` script is a graceful no-op when the
      CLI is missing, returns non-zero, or the cleaned text equals
      the input — so the hook is invisible in the common case).
      New `docs/CLAUDE-CODE.md` is the end-to-end reference
      (install shapes — one-liner / project-local / global merge /
      `dotnet run` source-mode swap; verification via `claude mcp
      list` + `claude mcp get`; auto-clean hook contract with
      project-local and `skills/install.sh --agent claude` paths
      and tuning tips; "what the agent sees" walkthrough;
      7-row troubleshooting table). README's MCP reference
      callout now points at `docs/CLAUDE-CODE.md` next to
      `docs/MCP.md`; the `## 🧠 Agent skills` section gains a
      parallel "Claude Code users get the integration pre-wired"
      callout with the one-liner; the docs-link footer adds a `🪝
      docs/CLAUDE-CODE.md` row. Build clean (0 warnings), 321
      tests all green; JSON validated with `node -e JSON.parse`,
      `auto-clean.js` exits 0 on empty / malformed / missing-CLI
      stdin.
- [x] **WR-S15 — OpenCode integration (`.opencode/` + slash commands)** —
      the project now ships a first-class OpenCode integration
      pre-wired into the repo: new
      `.opencode/skills/watermark-remover/SKILL.md` (master skill with
      routing table, CLI fallback, error contract), three slash
      commands under `.opencode/commands/` (`/wr-clean-text`,
      `/wr-clean-file`, `/wr-detect`), and a `watermarkremover`
      MCP entry in `.opencode/opencode.jsonc` (default
      `enabled: false`, matching the existing `github` pattern).
      The `permission.skill` allowlist now allows `watermark-remover`
      and the five per-format skills. `docs/MCP.md → OpenCode` is
      rewritten to match the actual OpenCode spec (MCP-via
      `opencode.jsonc`, skills under `.opencode/skills/`, commands
      under `.opencode/commands/`, markdown commands with
      `$ARGUMENTS`); the old `.opencode/mcp-config.json` recipe was
      not a real OpenCode file. README's MCP and Agent skills
      sections gain parallel OpenCode callouts. CHANGELOG updated
      under [Unreleased] → Added.
- [x] **WR-S14 — Agent skills (`skills/` + installer + `docs/SKILLS.md`)** —
      new top-level `skills/` directory that ships five drop-in skill
      packages (`watermark-clean-text`, `watermark-clean-markdown`,
      `watermark-clean-file`, `watermark-clean-image`, `watermark-detect`),
      each as a self-contained folder with `SKILL.md` (YAML-frontmatter
      trigger description + usage guide), POSIX `run.sh` and Windows
      `run.ps1` wrappers. Each `SKILL.md` covers trigger conditions,
      the canonical MCP tool call, the CLI fallback, 2-3 worked
      examples (EN + RU for text, before/after for markdown, file-type
      mapping table for file, mask guidance for image, vendor/kind
      interpretation for detect), error handling, language notes.
      New `skills/install.sh` and `skills/install.ps1` install any
      subset with one command (auto-detect probes CWD for
      `.opencode/`/`.claude/`/`.minimax/`, falls back to
      `~/.config/watermarkremover/skills/`; env overrides pin
      individual agents). The shell scripts and the new C#
      `SkillsInstallerTargetResolver` share the same resolution rules
      — 30 new xUnit tests cover every agent name + alias, home
      fallback, every env-override path, auto-detect probe, and
      argument validation. New `docs/SKILLS.md` is the full reference
      (resolution matrix, per-skill deep-dive, MCP-vs-skill,
      troubleshooting). README gains a new `## 🧠 Agent skills`
      section and a `docs/SKILLS.md` row. Build clean (0 warnings),
      321 tests total, all green.
- [x] **WR-S13 — `docs/MCP.md` reference** — new end-user-and-developer
      reference for the `serve-mcp` command. Architecture diagram
      (agent → transport → `WatermarkRemover.Mcp` → existing pipeline
      interfaces); transports (stdio default, Streamable HTTP
      stateless, legacy SSE noted as "not currently shipped"); tool
      reference for all 8 tools with full parameter tables,
      request/response JSON examples, and the output-block
      conventions (`TextContentBlock`, `EmbeddedResourceBlock` with
      `BlobResourceContents`, `ImageContentBlock`); configuration
      (`mcp:` block in `config.yaml`, CLI flag reference, resolution
      order CLI > config > default); install recipes for Claude Code
      (`claude mcp add`), OpenCode (`.opencode/mcp-config.json`),
      MiniMax Code, Cursor (`~/.cursor/mcp.json`), Continue
      (`~/.continue/config.json`), and Docker (`docker run -p 5090
      … serve-mcp --transport http`); troubleshooting table; and the
      canonical SDK reference links
      (https://github.com/modelcontextprotocol/csharp-sdk and
      https://csharp.sdk.modelcontextprotocol.io/) with the relevant
      entry points. README "🤖 MCP server" section gets a
      "Full reference: docs/MCP.md" pointer; the "📚 Documentation"
      section gains a dedicated `docs/MCP.md` row. Build clean (0
      warnings, 0 errors), 289 tests still green.
- [x] **WR-S12 — MCP server integration tests** — new
      `JsonRpcIntegrationTests` fixture in `WatermarkRemover.Mcp.Tests`
      wires a real `McpServer` host (the same composition root the
      `serve-mcp` stdio command uses — `AddWatermarkRemoverCore / Text /
      Metadata / Image / Mcp` + a local `IInpaintRunner` so no ONNX
      model is needed) to a paired `System.IO.Pipelines.Pipe` pair via
      `WithStreamServerTransport(input, output)`, then connects an
      `McpClient` over the matching `StreamClientTransport`. No
      subprocess, no socket, no port — the SDK's official in-process
      testing pattern. 11 new tests cover the full JSON-RPC surface:
      `Initialize_Handshake_ReturnsServerInfo`,
      `Initialize_Handsshake_AdvertisesToolsCapability`,
      `ToolsList_Returns8Tools`, `CleanText_RemovesZwsp`,
      `CleanMarkdown_StripsFrontmatter`,
      `DetectText_FindsVendorWatermark` (a Cyrillic homoglyph + ZWSP
      run triggers both Claude signatures),
      `InspectFile_ReturnsMetadataEntries` (tEXt chunk round-trip),
      `CleanFile_ReturnsBlobResource` (cleaned bytes as
      `EmbeddedResourceBlock` with `image/png` MIME),
      `CleanImage_ReturnsImageContentBlock` (PNG re-encode as
      `ImageContentBlock` with the 0x89 P N G signature verified),
      `DetectWatermark_ReturnsRegions` (real `MaskGenerator` over a
      32x32 fixture with a semi-transparent overlay), and
      `EmptyInput_ReturnsToolError` (McpException surfaces as
      `CallToolResult.IsError = true`, not a protocol error). The
      class-level `McpJsonRpcHost` (`IAsyncLifetime` +
      `IClassFixture<>`) starts the host, starts the client, and
      cleans up both at the end. Added `Microsoft.Extensions.Hosting`
      + bumped `Microsoft.Extensions.Logging.Abstractions` to
      `10.0.10` in `Directory.Packages.props` to match the transitive
      graph. Solution build clean, 0 warnings, 289 tests total
      (39 in `WatermarkRemover.Mcp.Tests`), all green.

- [x] **WR-S11 — `serve-mcp` CLI command + `mcp:` config** — new
      `watermarkremover serve-mcp` command (WR-S11) hosts the
      `WatermarkRemover.Mcp` server in two transports, selected via
      `--transport`: `stdio` (default) uses
      `Host.CreateApplicationBuilder()` + `AddWatermarkRemoverMcp()` +
      `.WithStdioServerTransport()` with **all** logging routed to
      stderr (per the MCP stdio spec) so the JSON-RPC stream on stdout
      stays clean; `http` uses
      `WebApplication.CreateBuilder()` + `AddWatermarkRemoverMcp()` +
      `.WithHttpTransport(o => o.Stateless = true)` + `app.MapMcp()`,
      reusing the same `X-API-Key` middleware and per-IP rate-limit
      pattern as the regular `serve` command (defaults: `--port 5090`
      — distinct from `serve`'s 5080 so the two can run side by side;
      `100 req/min/IP`; API key off). New `mcp:` section in
      `config.yaml` carries `transport` (default `stdio`), `host`,
      `port`, `api_key`, and `rate_limit.{permit_limit,window_seconds,
      queue_limit}` (the rate-limit inherits `server.rate_limit` when
      null). New `McpConfig` (with `Transport` / `Host` / `Port` /
      `ApiKey` / `RateLimit`) on `AppConfig`, plus `McpTransport` enum
      and `McpTransportExtensions.Parse` (accepts `stdio` / `pipe` and
      `http` / `streamable` / `streamable-http`; unknown values fail
      fast). `ModelContextProtocol.AspNetCore` 2.2.0 added to
      `Directory.Packages.props` and the CLI csproj. 43 new tests
      across 3 new fixtures: `McpConfigTests` (6 — default-value
      invariants), `McpTransportParseTests` (15 — every supported
      spelling + typo-rejection + round-trip), and
      `ServeMcpCommandTests` (22 — settings defaults, pre-flight
      validation, and the HTTP transport end-to-end via `TestServer`:
      `initialize` returns the right `serverInfo`, `tools/list` returns
      the 8 expected tool names, `--api-key` gates the MCP endpoint
      with `/health` exempt, unknown transport / bad rate-limit
      return exit code 1 with a clear error). `serve-mcp` added to
      the README commands table, the new `🤖 MCP server` README
      section (with the Claude Code one-liner), and the
      shell-completion script catalogue. 278 tests total, all green;
      0 build warnings.
- [x] **WR-S10 — MCP server core (`WatermarkRemover.Mcp`)** — new
      transport-agnostic class library exposing the full pipeline as
      eight Model Context Protocol tools (built on the official
      `ModelContextProtocol` C# SDK 2.2.0): `clean_text`,
      `clean_markdown`, `clean_file`, `clean_image`, `detect_text`,
      `detect_markdown`, `inspect_file`, `detect_watermark`. Each
      tool is a `[McpServerToolType]` static class with one
      `[McpServerTool]` method that calls the existing pipeline
      interface (`ITextCleaningPipeline`, `IMarkdownCleaner`,
      `IFileCleanerRouter`, `IImageCleaningPipeline`) — no new
      business logic. `clean_file` returns the cleaned bytes as an
      `EmbeddedResourceBlock` with the correct MIME; `clean_image`
      returns the cleaned PNG as an `ImageContentBlock`; the rest
      return `TextContentBlock` (text or JSON sidecar). New
      `AddWatermarkRemoverMcp` extension calls
      `AddMcpServer().WithToolsFromAssembly()` and applies the
      shared `ServerInfo` (name + version) to `McpServerOptions`.
      Transport binding (stdio or Streamable HTTP) is deliberately
      left to the host — `serve-mcp` in WR-S11 calls
      `.WithStdioServerTransport()` or `.WithHttpTransport()`
      before `RunAsync()`. 28 new tests in the new
      `WatermarkRemover.Mcp.Tests` project: 24 (happy + null-guard
      + error per tool), plus 4 DI tests asserting the builder,
      `ServerInfo`, 8 `McpServerTool` services, and full pipeline
      resolution. `ModelContextProtocol` 2.2.0 added to
      `Directory.Packages.props`. Solution build clean, 0 warnings,
      235 tests total, all green.
- [x] **WR-S9 — `watermarkremover --version`** — new global
      short-circuit that prints `watermarkremover <assembly version>` and
      exits `0` *before* `config.yaml` is loaded, Serilog is wired up,
      or the DI container is built. Three call shapes are accepted:
      `--version` (long form), `-V` (uppercase short), and a bare
      `-v` (lowercase short, only when it is the only arg). Backed by
      the new `WatermarkRemover.CLI.Infrastructure.CliShortCircuits`
      helper (which reads `VersionInfo.Current` — sourced from
      `AssemblyInformationalVersionAttribute` on the entry assembly)
      and the new `<Version>1.0.0</Version>` /
      `<InformationalVersion>1.0.0</InformationalVersion>` properties
      on the CLI csproj. 12 new tests in `VersionInfoTests` /
      `CliShortCircuitsTests`: the value is never empty or
      whitespace-padded, the fallback string is stable, all three call
      shapes exit `0` and write `watermarkremover {version}`, the
      short-circuit still fires when mixed with other args,
      non-version invocations (and `-v` paired with another arg) fall
      through to the regular `CommandApp` path, and `null` args
      throw. README "Global options" table updated. 207 tests total,
      all green; 0 build warnings.
- [x] **WR-S8 — `POST /detect/markdown` endpoint** — new `POST /detect/markdown`
      route in `ServeEndpointMapper.cs` (consumed by `serve`) that takes the
      same `{ markdown, stripAll }` body as `POST /clean/markdown` and returns
      `AiArtifact[]` — the same detector output the `detect-markdown` CLI
      command prints. `stripAll` is intentionally ignored: detection uses a
      fixed detector set, not the cleaning toggles. 400 on empty body, 401 when
      `--api-key` is set and the `X-API-Key` header is missing (consistent
      with the rest of the API). 4 new tests in `HttpEndpointTests`:
      happy path (frontmatter + AI signature both reported), 400 on empty
      body, 401 when auth is required, and OpenAPI spec includes the route.
      README HTTP API table updated.
- [x] **WR-S7 — Full markdown config surface** — every public toggle
      on `MarkdownCleanOptions` is now reachable from `config.yaml`
      (the previous 12-key surface grew to all 21). New
      `MarkdownCleanOptions.From(MarkdownConfig)` static factory in
      `WatermarkRemover.Core.Models` is the single source of truth for
      the binding — `clean-markdown`, `clean-all`, and the HTTP
      `POST /clean/markdown` endpoint all consume it. `clean-markdown`
      now reads the full config baseline; CLI flags
      (`--strip-all` / `--strip-code-fences` / `--strip-links`) override
      on a per-key basis. `docs/CONFIGURATION.md` documents every key
      with its default and one-line description. 30 new tests in
      `MarkdownConfigTests` and `ConfigYamlMarkdownTests` cover: every
      public boolean on `MarkdownCleanOptions` is surfaced in
      `MarkdownConfig`, the defaults stay in lockstep, `From()`
      round-trips every toggle, `--strip-all` enables every toggle,
      and a smoke test loads `src/config.yaml` and asserts all 21 keys
      are present. 187 tests total, all green.
- [x] **WR-S6 — Shell completion scripts** — new `completions --shell
      <bash|zsh|powershell|fish>` command emits a static completion script
      for the requested shell. `ShellCompletionScripts` (Infrastructure)
      holds the single source of truth — a curated command list and
      best-effort per-command option hints — and emits well-formed scripts
      that are namespaced to `watermarkremover` so they don't interfere
      with anything else. Install via
      `... --shell bash | sudo tee /etc/bash_completion.d/watermarkremover > /dev/null`
      (bash), drop the zsh script into
      `$(brew --prefix)/share/zsh/site-functions/_watermarkremover`, append
      the PowerShell block to `$PROFILE.CurrentUserAllHosts`, or save the
      fish script to `~/.config/fish/completions/watermarkremover.fish`.
      New `docs/SHELL-COMPLETION.md` covers all four installs plus a
      troubleshooting section. 17 new tests in `CompletionsCommandTests`:
      every shell renders a script that names the main sub-commands, the
      bash script registers `complete -F _watermarkremover`, the zsh
      script starts with `#compdef watermarkremover`, the PowerShell
      script uses `Register-ArgumentCompleter`, the fish script uses
      `complete` builtins, an unknown shell throws, the command returns 1
      on an unknown / empty shell and 0 with a valid script on stdout.
      155 tests total, all green.
- [x] **WR-S4 — `WatermarkRemover.CLI.Tests` HTTP coverage** — the
      `serve` command's HTTP surface now has end-to-end coverage via
      `Microsoft.AspNetCore.TestHost` (`Microsoft.AspNetCore.Mvc.Testing
      10.0.11`). The eight endpoints were extracted out of
      `ServeCommand` into a new public static
      `ServeEndpointMapper` (with companion `MountSwagger` and
      `MountStaticUi` helpers) so tests can host them in-memory without
      binding Kestrel. 11 new tests in `HttpEndpointTests`: `/health`
      200 + status body, `/clean/text` happy path (Layer A strips
      U+200B), `/clean/text` 400 on empty body, `/clean/text` 401 when
      `--api-key` is set and `X-API-Key` is missing, `/clean/text` 200
      with the right key, `/health` exempt from the key check, Swagger
      UI HTML at `/swagger/index.html`, OpenAPI JSON at
      `/swagger/v1/swagger.json`, `/` 200 with bundled `wwwroot/`,
      `/` 404 when `--no-ui`, `/` 404 when `wwwroot/` is absent.
      `TextRequest` and `MarkdownRequest` moved to top-level types in
      `WatermarkRemover.CLI.Commands` so the mapper and tests can
      reference them. `WatermarkRemover.CLI` now also takes
      `ILogger<ServeCommand>` for the static-UI mount warnings.
      138 tests total, all green.
- [x] **WR-S3 — `clean-all` auto-routing command** — new CLI command
      `clean-all <path>` walks a file or directory and dispatches each file
      to the right pipeline: `.md`/`.markdown` → markdown cleaner,
      router-supported extensions (JPEG/PNG/PDF/DOCX/HTML/WebP) → metadata
      cleaner, `.txt`/`.text`/`.log` and no-extension files → text pipeline
      (Layers A/B/C). Unknown binary extensions (`.bin`, `.exe`, `.mp4`, …)
      are skipped with a warning rather than being fed to the text
      pipeline. Supports `--recursive` and `--dry-run`; honours the global
      `--json`. New `CleanAllClassifier` (pure routing decision) +
      `CleanAllCommand`; new `WatermarkRemover.CLI.Tests` project with
      33 tests (classifier unit + end-to-end command with temp fixtures
      covering happy path, recursive flag, dry-run, single-file mode,
      binary-skip, missing path, ordering, null guards). 127 tests total,
      all green.
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