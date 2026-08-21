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

### WR-S4. [~] `WatermarkRemover.CLI.Tests` project (WebApplicationFactory for HTTP)

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

## Phase 6 — Agent integration (MCP, skills, plugins)

These tasks build the agent-integration layer so WatermarkRemover works
inside Claude Code, OpenCode, MiniMax Code, Cursor, Continue, and any
MCP-compatible host. Full specs in [BACKLOG.md → P6](./BACKLOG.md#p6--agent-integration-mcp-skills-plugins).
Pick in order — MCP server must land before skills and plugins can use it.

### WR-S10. [ ] MCP server core (`WatermarkRemover.Mcp` project)

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

### WR-S11. [ ] `serve-mcp` CLI command + MCP config

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

### WR-S12. [ ] MCP server tests

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

### WR-S13. [ ] MCP server docs (`docs/MCP.md`)

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

### WR-S14. [ ] Agent skills (`skills/` directory + installer)

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

### WR-S15. [ ] OpenCode plugin

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

### WR-S16. [ ] Claude Code integration

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

### WR-S17. [ ] MiniMax Code integration

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