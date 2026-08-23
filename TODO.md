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

### WR-S22. [x] PPTX + XLSX metadata cleaners

- **Why:** BACKLOG P1 — `.pptx` and `.xlsx` files (Microsoft Office Open
  XML, the same ZIP-of-XML container family as `.docx`/`.epub`) are
  not currently supported; `clean-file` returns "unsupported" for them.
  AI-generated Office documents ship with the same carrier metadata
  the existing DOCX cleaner already strips: `dc:creator` /
  `dc:title` / `cp:lastModifiedBy` in `docProps/core.xml`, plus the
  `docProps/app.xml` extended-properties part (Application, Company,
  Manager, Template) and `docProps/custom.xml` custom-properties part.
  The Open XML SDK already in `Directory.Packages.props` is the right
  tool for the job — `DocumentFormat.OpenXml.Packaging.PresentationDocument`
  and `SpreadsheetDocument` expose the same `PackageProperties` /
  `ExtendedFilePropertiesPart` / `CustomFilePropertiesPart` shape as
  `WordprocessingDocument`, so the new cleaners reuse a shared internal
  helper and add the format-specific pieces (PPT slide comments +
  comment-author list, XLS threaded comments + shared-string authorship).
  Fills the two biggest gaps in the Office-Open-XML family.
- **Scope:** `src/WatermarkRemover.Metadata/`,
  `src/tests/WatermarkRemover.Metadata.Tests/`
- **Files to touch:**
  - New `src/WatermarkRemover.Metadata/OpenXmlCoreMetadataCleaner.cs` —
    internal static helper with `ClearCoreProperties(OpenXmlPackage pkg,
    List<MetadataEntry> removed)`, `DeleteExtendedProperties(OpenXmlPackage
    pkg, List<MetadataEntry> removed)`, `DeleteCustomProperties(OpenXmlPackage
    pkg, List<MetadataEntry> removed)`, plus inspect-side counterparts
    `InspectCoreProperties(pkg)`, `HasExtendedProperties(pkg)`,
    `HasCustomProperties(pkg)`. Each just touches the same three parts
    the DOCX cleaner already mutates — no OpenXml-specific business
    logic, so the helper is safe to share between all three formats.
  - New `src/WatermarkRemover.Metadata/PptxMetadataCleaner.cs` — opens
    with `PresentationDocument.Open`, calls the shared helper to clear
    core/extended/custom, then walks `CommentPart` instances on every
    `SlidePart` and deletes the comment XML (the author + initials of
    every comment are pure authorship metadata), and deletes every
    `CommentAuthorPart` so the slide has no `commentAuthor` left. The
    slide content itself (text, shapes, images) is preserved verbatim.
  - New `src/WatermarkRemover.Metadata/XlsxMetadataCleaner.cs` — opens
    with `SpreadsheetDocument.Open`, calls the shared helper, then
    deletes the `WorkbookPart.ThreadedCommentAuthorsPart` (it stores
    per-thread author display names) and clears every
    `WorkbookPart.Workbook.Descendants<ThreadedComment>()` element.
    Cell values + formulas + shared string table are preserved.
  - `src/WatermarkRemover.Metadata/DependencyInjection.cs` — register
    both new cleaners in `AddWatermarkRemoverMetadata`.
  - `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` —
    update `<PackageDescription>` and `<PackageTags>` to mention PPTX
    + XLSX.
  - `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` —
    `WritePptxWithMetadata(path, creator, application, withComment)`
    helper that uses `PresentationDocument.Create` + `AddNewPart<SlidePart>`
    + `SlideCommentsPart` to emit a tiny but real `.pptx`; and
    `WriteXlsxWithMetadata(path, creator, application, withThreadedComment)`
    that uses `SpreadsheetDocument.Create` + `WorksheetPart` +
    `ThreadedCommentAuthorsPart` to emit a tiny but real `.xlsx`.
  - `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` —
    ≥ 12 new tests (≥ 6 per cleaner) — see Acceptance.
  - `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` —
    add `.pptx`/`.PPTX`/`.xlsx`/`.XLSX` to the
    `IsSupported_KnownExtensions_ReturnsTrue` theory, add
    `Resolve_Pptx_ReturnsPptxCleaner` + `Resolve_Xlsx_ReturnsXlsxCleaner`
    facts, add `.pptx` + `.xlsx` to `BuildRouter()` + the
    `SupportedExtensions_AggregatesAllCleaners` assertion.
  - `README.md` — line 49 supported-formats list + the metadata section
    + the docs/INSTALLATION.md mention (whichever exists).
  - `BACKLOG.md` — flip `WR-P106` to `[x]`.
  - `CHANGELOG.md` — new "Added" entry under `[Unreleased]`.
- **Acceptance:**
  - `dotnet build` clean (0 warnings, 0 errors).
  - `dotnet test` clean; at least 12 new tests (≥ 6 per cleaner) covering:
    - PPTX Inspect finds `dc:creator` and `Application` (`app.xml`) on a
      fixture with both.
    - PPTX Clean clears `core.xml` (re-inspect shows no Creator), deletes
      `app.xml`, deletes `custom.xml` if present, and removes every
      `CommentPart` + every `CommentAuthorPart` so the slide is
      comment-free.
    - PPTX output is a valid OpenXml container that
      `PresentationDocument.Open(...)` re-opens without throwing.
    - PPTX slide text / shape count is preserved (open the cleaned
      file and assert the original placeholder text is still in the
      shape tree).
    - PPTX corrupt / non-pptx input throws `MetadataStripException`.
    - PPTX `CanHandle(".pptx")` true, `CanHandle(".PPTX")` true.
    - XLSX Inspect finds `dc:creator` and `Application` on a fixture.
    - XLSX Clean clears `core.xml`, deletes `app.xml` and `custom.xml`.
    - XLSX output is a valid OpenXml container that
      `SpreadsheetDocument.Open(...)` re-opens.
    - XLSX cell values are preserved (re-open the cleaned workbook and
      assert the fixture's value is still in `Sheet1.Cells["A1"]`).
    - XLSX corrupt / non-xlsx input throws `MetadataStripException`.
    - XLSX `CanHandle(".xlsx")` true, `CanHandle(".XLSX")` true.
  - File-routing tests pass.
- **Risks:** None — pure managed code on top of the already-pinned
  `DocumentFormat.OpenXml` 3.2.0 NuGet. The shared helper
  (`OpenXmlCoreMetadataCleaner`) takes an `OpenXmlPackage` which is
  the common base class of `WordprocessingDocument`,
  `PresentationDocument`, and `SpreadsheetDocument`, so DOCX could
  later be refactored to call it too — out of scope for this tick.
- **Backlog ref:** WR-P106

### WR-S21. [x] EPUB metadata cleaner

- **Why:** BACKLOG P1 — `.epub` files (zip-of-XHTML) are not currently
  supported; `clean-file` returns "unsupported" for them. AI-generated
  EPUBs ship with OPF `<dc:creator>` / `<dc:contributor>` /
  `<meta property="dcterms:modified">` and similar watermarks that
  identify the generator. Stripping them via zip-rewrite is a small
  well-scoped first format addition that fills the gap.
- **Scope:** new `src/WatermarkRemover.Metadata/EpubMetadataCleaner.cs`
- **Files to touch:**
  - New `src/WatermarkRemover.Metadata/EpubMetadataCleaner.cs` — uses
    `System.IO.Compression.ZipArchive` to open the input, locates
    `META-INF/container.xml` to find the OPF path, parses the OPF as
    XML, removes all `<dc:*>` (except `dc:identifier` kept as a
    freshly-generated UUID so the EPUB stays structurally valid) and
    all `<meta>` elements, then writes a new zip preserving every
    other entry verbatim (XHTML / CSS / images). The `mimetype`
    entry stays first, uncompressed, as the EPUB spec requires.
  - `src/WatermarkRemover.Metadata/DependencyInjection.cs` —
    `services.AddSingleton<IFileMetadataCleaner, EpubMetadataCleaner>();`
  - `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` —
    update `<PackageDescription>` and `<PackageTags>` to mention EPUB
  - `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` —
    `WriteEpubWithMetadata(path, ...)` helper that writes a valid
    minimal EPUB zip with the chosen OPF metadata
  - `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` —
    at least 6 tests (see Acceptance)
  - `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` —
    add `.epub` to `IsSupported_KnownExtensions_ReturnsTrue`, a new
    `Resolve_Epub_ReturnsEpubCleaner` fact, and include the cleaner
    in `BuildRouter()`
  - `README.md` — line 49 list + the metadata section + roadmap tick
  - `BACKLOG.md` — flip `WR-P105` to `[x]`
  - `CHANGELOG.md` — new "Added" entry under `[Unreleased]`
- **Acceptance:**
  - `dotnet build` clean
  - `dotnet test` clean; at least 6 new EPUB tests covering:
    - Inspect finds `dc:creator` and `dc:title` from a fixture
    - Clean removes all `<dc:*>` elements except `dc:identifier`
      (kept as a freshly-generated UUID so the EPUB stays valid)
    - Clean removes all `<meta>` elements
    - The output is a valid ZIP (re-opens with `ZipArchive`)
    - The `mimetype` entry remains first and uncompressed
    - The `META-INF/container.xml` is preserved unchanged
    - Corrupt / non-EPUB input throws `MetadataStripException`
    - `CanHandle(".epub")` true, `CanHandle(".EPUB")` true
  - File-routing tests pass
- **Risks:** None — pure managed code, `System.IO.Compression.ZipArchive`
  is part of the .NET runtime. `System.Xml.Linq` is also in the BCL.
- **Backlog ref:** WR-P105

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

### WR-S18. [x] Cursor / Continue MCP config + npm package

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

### WR-S19. [x] VS Code extension (MCP-based)

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

### WR-S20. [x] MCP packaging + Docker for MCP

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

### WR-S23. [x] DeepSeek / Grok / Mistral vendor detectors (Layer C)

- **Why:** BACKLOG P1 — the `IAiTextWatermarkDetector` registry currently
  ships three vendors (Claude, Gemini, OpenAI). DeepSeek, Grok, and
  Mistral all carry detectable, character-level traces in their outputs
  and the README's P1 roadmap already lists this work as the next item
  (README:740). Layer C runs last, so each new detector is purely
  additive — no other pipeline changes. All three vendors are pure-managed
  detections (no key, no remote call, no model), so the cost per detector
  is one file in `Vendors/` and one DI line, matching the existing
  pattern.
- **Scope:** `src/WatermarkRemover.Text/Vendors/`,
  `src/WatermarkRemover.Text/DependencyInjection.cs`,
  `src/tests/WatermarkRemover.Text.Tests/VendorDetectorTests.cs`,
  `README.md`, `BACKLOG.md`, `CHANGELOG.md`.
- **Files to touch:**
  - New `src/WatermarkRemover.Text/Vendors/DeepSeekWatermarkDetector.cs`
    — `VendorName = "DeepSeek"`. Two pattern families:
    - **Reasoning-block**: literal `<` followed by `think` or
      `/think`, with optional `>` and matching close-tag. DeepSeek-R1
      routinely leaks its `<think>…</think>` reasoning trace into the
      final answer (the rendered chain-of-thought is meant to be
      stripped by the serving stack, but the user-visible copy often
      still carries the tags). Match each tag as a `WatermarkMatch`
      with pattern `reasoning-block`. Confidence 0.95 (very high —
      this string is not in natural prose).
    - **Fullwidth punctuation**: a code point in the
      `U+FF01`–`U+FF5E` range (full-width `!` to `~` — the CJK
      ASCII twin block) when the previous or next character is
      ASCII Latin. DeepSeek's training data has an over-representation
      of CJK text, so a full-width comma, period, or question mark
      sitting inside an otherwise Latin sentence is a strong stylistic
      fingerprint. Match each such code point as a `WatermarkMatch`
      with pattern `fullwidth-punctuation`. Confidence 0.7
      (a single full-width mark in a mostly-Latin passage is
      suspicious; a sentence of full-width is just Chinese).
    - `Remove`: strip the `<think>` / `</think>` tags verbatim
      (including any surrounding whitespace), and normalise each
      full-width code point in the `U+FF01`–`U+FF5E` range to its
      ASCII equivalent (`U+0021`–`U+007E`).
  - New `src/WatermarkRemover.Text/Vendors/GrokWatermarkDetector.cs`
    — `VendorName = "Grok"`. Two pattern families:
    - **Emoji burst**: 3+ consecutive emoji code points (anything in
      the BMP supplementary planes commonly classified as emoji —
      `U+1F300`–`U+1FAFF`, `U+2600`–`U+27BF`, `U+1F1E6`–`U+1F1FF`
      regional indicators, the `U+FE0F` variation selector).
      Grok's `grok-2` persona frequently injects 3–5 emoji in a row
      at the start of a response. Match each run with pattern
      `emoji-burst`. Confidence 0.6.
    - **Em-dash cluster**: 3+ consecutive `U+2014` em-dashes
      (sometimes with intervening zero-width joiners). Grok's
      outputs overuse em-dashes in a way the other vendors don't.
      Match the run with pattern `em-dash-cluster`. Confidence 0.7.
    - `Remove`: collapse each emoji burst to a single emoji
      (the first in the run), and collapse each em-dash cluster
      to a single em-dash. This keeps the document readable
      rather than stripping the whole fingerprint.
  - New `src/WatermarkRemover.Text/Vendors/MistralWatermarkDetector.cs`
    — `VendorName = "Mistral"`. One pattern family:
    - **Template-leak**: the literal Mistral chat-template
      markers that sometimes survive a renderer's prompt
      sanitisation. Six tokens, all case-sensitive: `[INST]`,
      `[/INST]`, `<<SYS>>`, `<</SYS>>`, `<s>`, `</s>`. Each is
      a 100% sure signal — these are never natural prose. Match
      each occurrence with pattern `template-leak`. Confidence 0.99.
    - `Remove`: drop the markers entirely. If `[INST]` immediately
      precedes user-style content and `[/INST]` immediately follows
      it, leave a single space between (so the cleaned sentence
      still has word boundaries), otherwise drop without a space.
  - `src/WatermarkRemover.Text/DependencyInjection.cs` — register
    the three new detectors next to the existing Claude / Gemini /
    OpenAI block (same `AddSingleton<IAiTextWatermarkDetector, …>()`
    pattern).
  - `src/WatermarkRemover.Text/WatermarkRemover.Text.csproj` — add
    `deepseek` and `grok` and `mistral` to the `<PackageTags>`
    list and mention all three in `<PackageDescription>`.
  - `src/tests/WatermarkRemover.Text.Tests/VendorDetectorTests.cs`
    — extend the existing `Detectors` member data, add at least
    **6 facts per new detector** (positive detection + removal +
    negative / "clean text" + at least one boundary case):
    - **DeepSeek** (6): reasoning-tag detected and removed;
      reasoning-tag content preserved; full-width comma flagged;
      full-width `~` flagged; clean text returns no matches;
      mixed Latin + full-width returns a single fullwidth match
      (not a run).
    - **Grok** (6): emoji burst detected; emoji burst collapsed
      to one emoji on Remove; em-dash cluster detected and
      collapsed; single emoji does NOT match; single em-dash
      does NOT match; clean text returns no matches.
    - **Mistral** (6): `[INST]…[/INST]` block detected and
      stripped; `<<SYS>>…<</SYS>>` detected and stripped;
      `<s>` / `</s>` sentence-piece markers detected and
      stripped; multiple `[INST]` occurrences each get their
      own `WatermarkMatch`; clean text returns no matches; the
      `Remove` call is a no-op for empty `matches` (boundary).
- **Acceptance:**
  - `dotnet build` clean (0 warnings, 0 errors).
  - `dotnet test` clean; at least **18 new tests** (6 per new
    detector) on top of the existing 35 Text.Tests, all green.
  - `Layer C` picks up all three new vendors automatically —
    a `clean-text` run on a fixture containing `<think>` /
    `[INST]` / an emoji burst must return cleaned text with
    those markers gone (verifiable through a single
    `Pipeline_RunsLayerCForAllRegisteredDetectors` test or
    via the existing `TextCleaningPipelineTests` suite).
  - Each detector's `VendorName` is exact (`"DeepSeek"`,
    `"Grok"`, `"Mistral"`) — the orchestrator's
    `detections.AddRange(matches)` carries that string through
    to JSON output and downstream consumers rely on it.
- **Risks:** None — pure managed code, no model download, no
  external dependency. The detectors' patterns are deliberately
  *high-precision, low-recall* (they'll miss some real DeepSeek /
  Grok / Mistral output, but they will not flag non-vendor text),
  which matches the existing Claude / Gemini / OpenAI detectors'
  posture. The README at line 740 already advertises this work
  on the P1 roadmap, so the user-facing description stays
  consistent.
- **Backlog ref:** WR-P111


---

## Blocked

*(empty — no item is currently waiting on user input)*

---

## In progress

*(empty — no tick is currently in progress; pick the next item from
the bottom of the sprint list or the top of the backlog.)*

---

### WR-S24. [x] ImageSharp → SkiaSharp migration (Layer Image + drop TIFF)

- **Why:** The 4.x bump of `SixLabors.ImageSharp` in commit a858589
  introduced two regressions that the cleanup of the previous tick
  couldn't address without a real rewrite: (a) the
  `Tiff_Clean_RemovesExifProfile_OutputIsValidTiff` test now fails
  because the v4 encoder reconstructs an `ExifIFD` even when the
  profile was nulled; (b) the eight `DotnetToolPackagingTests.Pack_*`
  tests fail because `dotnet pack` does a fresh build that hits the
  same code path with a stricter compiler setting. Both come from the
  same root cause: `ImageSharp 4.x` changed decode-and-re-encode
  semantics for TIFF, and the project is paying for staying on it.
  SkiaSharp (managed bindings over Google's Skia) handles the same
  use case without the TIFF-specific surprise — at the cost of TIFF
  support, which SkiaSharp doesn't have a codec for. The user
  explicitly chose "drop TIFF support" over keeping a parallel
  ImageSharp-for-TIFF-only path or pulling in Magick.NET.
- **Scope:** `src/WatermarkRemover.Image/`,
  `src/WatermarkRemover.Metadata/`,
  `src/WatermarkRemover.Mcp/Tools/CleanImageTool.cs`,
  `src/Directory.Packages.props`,
  plus all four image / metadata / Mcp test projects (commit 2)
- **Files to touch (commit 1 — API):**
  - `src/Directory.Packages.props` — drop `SixLabors.ImageSharp 4.1.1`,
    add `SkiaSharp 3.119.0` + `SkiaSharp.NativeAssets.Win32/Linux/macOS`
    for the cross-platform native binaries.
  - `src/WatermarkRemover.Image/WatermarkRemover.Image.csproj` — replace
    the `SixLabors.ImageSharp` reference with the three SkiaSharp
    native-asset packages (the managed `SkiaSharp` package comes
    transitively as a dependency of those).
  - `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` —
    drop the `SixLabors.ImageSharp` reference (the TIFF cleaner is
    retired in this tick); update the `<PackageDescription>` and
    `<PackageTags>` to drop the TIFF mention.
  - `src/WatermarkRemover.Image/IInpaintRunner.cs` — change
    `Image<Rgb24> Inpaint(Image<Rgb24>, Image<L8>)` →
    `SKBitmap Inpaint(SKBitmap, SKBitmap)`. Document the colour-type
    expectations in the XML doc (RGB or RGBA for the image, Gray8
    for the mask).
  - `src/WatermarkRemover.Image/MaskGenerator.cs` — rewrite the
    `Detect` / `BuildMask` / `BuildMaskWithRegions` /
    `ColorFrequencyPass` / `ExtractRegions` chain on top of
    `SKBitmap` (`Rgba8888`) and `ReadOnlySpan<SKColor>` via
    `MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan())`.
  - `src/WatermarkRemover.Image/ImageCleaningPipeline.cs` —
    rewrite the full pipeline (load → mask → resize → infer → blend
    → save) on top of SkiaSharp. Use `SKBitmap.Resize(info,
    SKSamplingOptions.Default)` (the `SKFilterQuality` overload is
    obsolete in 3.x), `SKImageFilter.CreateBlur(2f, 2f)` for the
    blend-mask soft edges, and `SKImage.FromBitmap(...).Encode(format,
    quality)` for the save step.
  - `src/WatermarkRemover.Image/LamaInpaintingService.cs` — switch
    the `Inpaint` body to iterate `SKBitmap` pixel spans (separate
    branches for `Rgb888x` and `Rgba8888` input colour types; the
    mask is required to be `Gray8`). The ONNX tensor marshalling
    logic and the NHWC/NCHW output detection are unchanged.
  - `src/WatermarkRemover.Mcp/Tools/CleanImageTool.cs` — replace
    the `Image<Rgba32> LoadAsync(...)` + `SaveAsync(..., new
    PngEncoder(), ...)` re-encode with `SKBitmap.Decode(path)` +
    `SKImage.FromBitmap(...).Encode(SKEncodedImageFormat.Png, 100)`.
  - `src/WatermarkRemover.Metadata/DependencyInjection.cs` — drop
    the `TiffMetadataCleaner` registration.
  - `src/WatermarkRemover.Metadata/TiffMetadataCleaner.cs` — delete
    the file.
  - `README.md` — drop TIFF from the supported-format list, the
    metadata section, and the P1 roadmap one-liner; mention the
    SkiaSharp move in a short note.
  - `BACKLOG.md` — mark WR-P131 `[x]` (this task); add a strikethrough
    note on the WR-P101 line that TIFF was retired here.
  - `CHANGELOG.md` — new "Changed" + "Removed" entry under
    `[Unreleased]`.
  - `TODO.md` — mark this section `[x]`; add a "Recently done"
    entry; mark the WR-S23 entry as the previous item in
    "Recently done".
- **Files to touch (commit 2 — tests):** every test project that
  uses `SixLabors.ImageSharp` (`Rgba32`, `L8`, `Rgb24`,
  `IPixel<>`, `Image<>`, `ExifProfile`, `PngEncoder`, the
  `TIFFMetadataCleaner` test class, the hand-crafted TIFF fixture,
  any other test that depends on the TIFF cleaner). Test counts
  before: 647 (81 + 56 + 9 + 155 + 39 + 307). Expected after:
  ~620 (TIFF cleaner suite removed, image-tests rewritten to
  use `SKBitmap` fixtures, Mcp tests updated to the new
  `IInpaintRunner` signature).
- **Acceptance:**
  - `dotnet build` clean (0 warnings, 0 errors) once commit 2 lands.
  - `dotnet test` clean; total test count drops only by the number of
    TIFF-specific tests we retire (the 14 TIFF facts + 3 router
    rows for `.tif`/`.tiff`/`.TIF`); every other test continues to
    pass on SkiaSharp.
  - `MaskGenerator` and `ImageCleaningPipeline` still produce
    pixel-identical (or visually equivalent) output to the ImageSharp
    baseline on the same fixture.
  - `IInpaintRunner.Inpaint(SKBitmap, SKBitmap)` is the new
    contract; the LaMa runner still degrades gracefully when the
    model file is missing.
- **Risks:** SkiaSharp's `GetPixelSpan` returns `Span<byte>`; the
  per-pixel accessors (`SKColor.Red` / `.Green` / `.Blue` / `.Alpha`)
  are simple byte fields, so the byte cast is sound. Resampling uses
  the new `SKSamplingOptions` API (the `SKFilterQuality` overload is
  obsolete in 3.x); we're not chasing pixel-perfect ImageSharp
  resampling, just visually close. The TIFF retirement is the
  largest user-facing change — anyone with `.tif`/`.tiff` files
  hitting `clean-file` will get an "unsupported format" error.
  This is the explicitly-chosen trade-off.
- **Backlog ref:** WR-P131

## Recently done

These were completed in the most recent sprint; they live here for context
but have already been moved to BACKLOG.md `[x]` and CHANGELOG.md `[Unreleased]`.

- [x] **WR-P111 — DeepSeek / Grok / Mistral vendor detectors (Layer C)
      (`src/WatermarkRemover.Text/Vendors/DeepSeekWatermarkDetector.cs` +
      `src/WatermarkRemover.Text/Vendors/GrokWatermarkDetector.cs` +
      `src/WatermarkRemover.Text/Vendors/MistralWatermarkDetector.cs` +
      `src/WatermarkRemover.Text/DependencyInjection.cs` +
      `src/WatermarkRemover.Text/WatermarkRemover.Text.csproj` +
      `src/tests/WatermarkRemover.Text.Tests/VendorDetectorTests.cs` +
      `README.md` + `BACKLOG.md` + `CHANGELOG.md` + `TODO.md`)** —
      `IAiTextWatermarkDetector` registry now ships six vendors
      instead of three. Three pure-managed detector files in
      `WatermarkRemover.Text/Vendors/`, registered in DI alongside
      the existing Claude / Gemini / OpenAI detectors. Each
      detector is *high-precision, low-recall* (it flags what's
      certainly vendor-typical, doesn't try to invert the
      per-token statistical watermark, which needs the secret
      key): `DeepSeekWatermarkDetector` flags the `<think>` /
      `</think>` reasoning-block leak (case-insensitive, optional
      trailing `>`, conf 0.95) and the fullwidth-ASCII CJK
      fingerprint `U+FF01..U+FF5E` sitting between two ASCII
      Latin letters (conf 0.7); `GrokWatermarkDetector` flags
      3+ emoji bursts (BMP misc-symbols blocks + supplementary
      plane via UTF-16 surrogate detection, ZWJ-tolerant run
      boundaries, conf 0.6) and 3+ em-dash clusters (conf 0.7);
      `MistralWatermarkDetector` flags the six literal chat-
      template markers (`[INST]`, `[/INST]`, `<<SYS>>`, `<</SYS>>`,
      `<s>`, `</s>`, all case-sensitive, each a 100% sure
      signal, conf 0.99). `Remove` semantics: DeepSeek strips
      the tags verbatim and folds each fullwidth code point to
      its ASCII twin; Grok collapses each run to a single emoji
      / em-dash (soft normalisation, matches the MarkdownCleaner's
      emoji-sign-off posture); Mistral drops the markers, splicing
      a single space between adjacent non-whitespace characters.
      **21 new xUnit tests** in `WatermarkRemover.Text.Tests`
      (6 per detector: positive detection + removal + clean-text
      negative + boundary case). Build clean (0 warnings, 0
      errors), **647 xUnit tests** total (81 + 56 + 9 + 155 + 39
      + 307). The 1 remaining Metadata failure
      (`Tiff_Clean_RemovesExifProfile_OutputIsValidTiff`) and the
      8 CLI failures (`DotnetToolPackagingTests.Pack_*`) are
      pre-existing ImageSharp 4.x upgrade regressions — the
      ImageSharp decode-and-re-encode path that the TIFF cleaner
      relies on changed semantics, and `dotnet pack` is doing a
      fresh build that hits the same path. Both will be addressed
      by the upcoming ImageSharp → SkiaSharp migration tick.

- [x] **WR-P108 — MP4 / MOV / M4V / M4A / M4B / M4P / 3GP / 3G2 metadata cleaner
      (`src/WatermarkRemover.Metadata/Mp4MetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/IsoBoxReader.cs` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `README.md` + `BACKLOG.md` + `CHANGELOG.md`)** — `.mp4` /
      `.mov` / `.m4v` / `.m4a` / `.m4b` / `.m4p` / `.3gp` / `.3g2`
      files now flow through the same metadata-strip pipeline as
      JPEG / PNG / WebP / TIFF / HEIF / AVIF / PDF / DOCX / PPTX /
      XLSX / HTML / EPUB / RTF. The cleaner is a pure-managed
      ISOBMFF box walker built on a new internal `IsoBoxReader`
      helper (shared 4CC box-header decoder that honours the 8-byte
      `largesize` extension). It validates the `<ftyp>` brand
      against the MP4 / MOV / 3GP / iTunes / QuickTime
      compatible-brands list and walks the top-level boxes:
      `ftyp` / `mdat` / `free` / `skip` are streamed through
      bit-for-bit (the `mdat` bitstream is never materialised, so
      multi-GB videos are safe); `moov` is rewritten so `mvhd` /
      `trak` / `edts` / `mvex` are preserved but `udta` (the
      `©xyz` GPS atom, `©mak` / `©mod` / `©swr` device make /
      model / software, `©day` / `©nam` / `©ART` / `©cmt`
      authorship) is stripped wholesale; any `meta` FullBox has its
      QuickTime `keys` key-namespace index and `ilst` data list
      stripped, plus the same EXIF / XMP / ICC policy as the AVIF
      cleaner. **15 new xUnit tests** in
      `WatermarkRemover.Metadata.Tests` (14 MP4 cleaner tests + 1
      router fact). Build clean (0 warnings, 0 errors), **626
      xUnit tests total** (81 + 35 + 9 + 155 + 39 + 307), all
      green.
- [x] **WR-P107 — RTF metadata cleaner
      (`src/WatermarkRemover.Metadata/RtfMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `README.md` + `BACKLOG.md` + `CHANGELOG.md`)** — `.rtf` files now
      flow through the same metadata-strip pipeline as JPEG / PNG / WebP /
      TIFF / HEIF / AVIF / PDF / DOCX / PPTX / XLSX / HTML / EPUB. The
      cleaner is a pure-managed character-stream parser: it reads the
      file as ASCII, validates the `{\rtf` magic, walks the stream
      control-word-by-control-word, and strips the canonical
      authorship-and-provenance control words — `\author`, `\company`,
      `\manager`, `\category`, `\keywords`, `\subject`, `\title`,
      `\comment`, `\doccomm`, `\hlinkbase`, `\generator`, `\operator`,
      `\version`, `\edmins`, `\nofpages`, `\nofwords`, `\nofchars`,
      `\nofcharsws`, `\id` — plus the compound time-table entries
      `\creatim` / `\revtbl` / `\printim` / `\buptim` (each followed by
      sub-control words like `\yr\mo\dy\hr\min\sec`). The RTF body,
      font table, colour table, stylesheet, headers / footers, and
      visible text content are all preserved byte-for-byte. Re-validates
      the output by re-checking the `{\rtf` magic and balanced braces,
      and surfaces `MetadataStripException` for corrupt / non-RTF
      inputs. The router is updated: `AddWatermarkRemoverMetadata`
      registers the new cleaner; `FileCleanerRouter` resolves `.rtf`
      (case-insensitive) to it. The package description and tags on
      `WatermarkRemover.Metadata.csproj` now list RTF. README adds RTF
      to the supported-format list, the metadata section, the project
      tree, and the P1 roadmap one-liner. **14 new xUnit tests** in
      `WatermarkRemover.Metadata.Tests` (11 RTF cleaner tests covering
      Inspect / Clean / compound stripping / output validity / content
      preservation / round-trip / corrupt input / missing file /
      `CanHandle`; plus 3 router rows / facts for `.rtf`/`.RTF`).
      Build clean (0 warnings, 0 errors), **606 xUnit tests total**
      (81 + 35 + 9 + 135 + 39 + 307), all green.

- [x] **WR-P106 — PPTX + XLSX metadata cleaners
      (`src/WatermarkRemover.Metadata/OpenXmlCoreMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/PptxMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/XlsxMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `README.md` + `BACKLOG.md` + `CHANGELOG.md`)** — `.pptx` and
      `.xlsx` files now flow through the same metadata-strip pipeline as
      JPEG / PNG / WebP / TIFF / HEIF / AVIF / PDF / DOCX / HTML / EPUB.
      Both new cleaners are built on the same Open XML SDK 3.2.0 already
      pinned in `Directory.Packages.props`. A new internal shared helper
      `OpenXmlCoreMetadataCleaner` mutates the parts every Microsoft
      Open XML format ships (`core.xml` / `app.xml` / `custom.xml`)
      through the package's `Parts` collection, so the helper works
      against the common `OpenXmlPackage` base class without needing
      per-document-type accessors. `PptxMetadataCleaner` then walks
      every per-slide `PowerPointCommentPart` + the presentation-wide
      `authorsPart` (`PowerPointAuthorsPart`) and deletes them, so
      slide comments and their authorship go away with the rest of
      the metadata; the slide content (text, shapes, layout) is
      preserved byte-for-byte. `XlsxMetadataCleaner` then walks
      every per-worksheet `WorksheetCommentsPart` +
      `WorksheetThreadedCommentsPart` and the workbook-wide
      `CommentAuthorsPart` and deletes them all, so cell comments
      and their authorship go away too; cell values, formulas, and
      the shared string table are preserved. Both cleaners
      re-validate the output as a real `PresentationDocument` /
      `SpreadsheetDocument` and surface `MetadataStripException` for
      corrupt / non-Office-Open-XML inputs. The router is updated:
      `AddWatermarkRemoverMetadata` registers both new cleaners;
      `FileCleanerRouter` resolves `.pptx` and `.xlsx` (case-
      insensitive) to them. The package description and tags on
      `WatermarkRemover.Metadata.csproj` now list PPTX + XLSX.
      README adds the two formats to the supported-format list, the
      metadata section, and the project-tree one-liner. **20 new
      xUnit tests** in `WatermarkRemover.Metadata.Tests` (7 PPTX
      cleaner tests + 7 XLSX cleaner tests covering Inspect / Clean
      / round-trip / content preservation / corrupt input / missing
      file / `CanHandle`; plus 6 router theory + fact rows for
      `.pptx`/`.PPTX`/`.xlsx`/`.XLSX`). Build clean (0 warnings,
      0 errors), **592 xUnit tests total** (81 + 35 + 9 + 121 + 39
      + 307), all green.

- [x] **WR-P105 — EPUB metadata cleaner
      (`src/WatermarkRemover.Metadata/EpubMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `README.md` + `BACKLOG.md` + `CHANGELOG.md`)** — `.epub` files
      now flow through the same metadata-strip pipeline as JPEG / PNG /
      WebP / TIFF / PDF / DOCX / HTML. The cleaner is an OCF
      zip-rewriter: it opens the input as a
      `System.IO.Compression.ZipArchive`, locates the OPF package via
      `META-INF/container.xml`, parses the OPF with `System.Xml.Linq`,
      and rebuilds the archive child-by-child — stripping every
      `<dc:*>` Dublin Core element except `dc:identifier` (which is
      kept but with a freshly-generated `urn:uuid:<guid>` so the
      output remains a structurally valid EPUB) and every `<meta>`
      element (both `property="…"` and `name="…"` flavours, including
      the AI-watermark-friendly `dcterms:modified` /
      `ibooks:specified-fonts` entries). The canonical `mimetype`
      entry stays first, **uncompressed** (`CompressionLevel.NoCompression`),
      so the output is a valid OCF container that every EPUB reader
      can open; every other entry (XHTML chapters, CSS, images,
      fonts, NCX) is copied through the rewrite byte-for-byte.
      Header validation rejects files that lack a `mimetype` entry
      or whose `mimetype` content is not the literal string
      `application/epub+zip`; container / OPF XML well-formedness is
      enforced; `IndexOutOfRangeException` / `InvalidDataException` /
      generic exceptions during the zip walk are translated into the
      project-wide `MetadataStripException`. The router is wired up:
      `IFileMetadataCleaner` registration in
      `AddWatermarkRemoverMetadata` is updated; `FileCleanerRouter`
      resolves `.epub` (case-insensitive) to the new cleaner; the
      package description and tags on `WatermarkRemover.Metadata.csproj`
      now list EPUB. **20 new xUnit tests** in
      `WatermarkRemover.Metadata.Tests` (10 cleaner tests covering
      inspect, full-report clean, output-is-valid-zip,
      mimetype-stays-first-uncompressed, container-and-chapter
      byte-equal-through-pass, post-pass inspect, corrupt file,
      missing-mimetype, missing file, `CanHandle` recognition; plus
      router tests: new `Resolve_Epub_ReturnsEpubCleaner` fact +
      `.epub` / `.EPUB` rows added to the
      `IsSupported_KnownExtensions_ReturnsTrue` theory, and
      `SupportedExtensions_AggregatesAllCleaners` updated to include
      `.epub`). Build clean (0 warnings, 0 errors), **572 xUnit
      tests** (81 + 35 + 101 + 9 + 39 + 307), all green.

- [x] **WR-P104 — AVIF metadata cleaner
      (`src/WatermarkRemover.Metadata/AvifMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `BACKLOG.md` + `CHANGELOG.md`)** — `.avif` files now flow
      through the same metadata-strip pipeline as JPEG / PNG / WebP /
      TIFF / PDF / DOCX / HTML / HEIF. AVIF reuses the same ISOBMFF
      container as HEIF, so the new cleaner mirrors the HEIF walker
      verbatim: it parses the top-level `ftyp` / `meta` / `mdat` boxes
      and rebuilds `meta` child-by-child, stripping the 4CC `Exif`
      box, the Apple-UUID EXIF carrier, the XMP-UUID carrier, the
      `mime`-box XMP carrier, and the `colr` `rICC` / `prof` ICC
      profiles (all gated by the standard `MetadataCleanOptions`
      toggles; `nclx` is preserved as structural colour info).
      Header validation accepts the three AVIF brands `avif` /
      `avis` / `mif1` and rejects HEIC-only ftyps so HEIC files are
      not silently treated as AVIF. The 8-byte `largesize` extension
      is honoured; `size == 0` is refused; walker exceptions are
      translated into `MetadataStripException`. **23 new xUnit
      tests** in `WatermarkRemover.Metadata.Tests` (20 new cleaner
      tests + 2 new `IsSupported` theory rows + 1 new `Resolve_Avif`
      fact).

- [x] **WR-P101 — TIFF metadata cleaner
      (`src/WatermarkRemover.Metadata/TiffMetadataCleaner.cs` +
      `src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj` +
      `src/WatermarkRemover.Metadata/DependencyInjection.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/FileCleanerRouterTests.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/TestFixtures.cs` +
      `src/tests/WatermarkRemover.Metadata.Tests/MetadataCleanerTests.cs` +
      `BACKLOG.md` + `CHANGELOG.md`)** — `.tif` and `.tiff` files now
      flow through the same metadata-strip pipeline as JPEG / PNG / WebP /
      PDF / DOCX / HTML. The cleaner loads the TIFF via ImageSharp,
      walks every frame, and clears the EXIF / XMP / IPTC / ICC
      profiles on the ones that carry the corresponding content;
      ImageSharp's `TiffEncoder` re-emits the file with the structural
      IFD0 tags (Width / Length / BitsPerSample / etc.) re-derived
      from the frame dimensions, so the cleaned file is a valid
      single-image TIFF. Both `II` and `MM` byte-order markers, plus
      classic-TIFF (magic 42) and BigTIFF (magic 43) headers, are
      accepted at the byte-level check; ImageSharp decode-time
      exceptions are translated into the project-wide
      `MetadataStripException`. The hand-crafted `BuildHandCraftedTiffWithExif`
      test fixture works around a known ImageSharp 3.1.12 limitation
      (its own TIFF encoder writes EXIF inline in IFD0, so its decoder
      can't read the EXIF back from its own output) by constructing
      a spec-compliant little-endian grayscale TIFF with a proper
      `ExifIFD` sub-IFD. **14 new xUnit tests** in
      `WatermarkRemover.Metadata.Tests` (router `.tif` / `.tiff` /
      `.TIF` cases + 11 cleaner tests covering inspect / clean /
      re-inspect, no-metadata no-op, default-options preserves color
      profile, corrupt / non-TIFF / BigTIFF header edge cases, missing
      file, and the supported-extensions contract). Build clean (0
      warnings, 0 errors), **511 xUnit tests** (81 + 40 + 9 + 35 + 39
      + 307), all green.

- [x] **WR-P011 — `dotnet tool` packaging
      (`src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj` +
      `src/WatermarkRemover.CLI/README.md` +
      `src/tests/WatermarkRemover.CLI.Tests/DotnetToolPackagingTests.cs` +
      `BACKLOG.md` + `CHANGELOG.md` + `README.md`)** — the
      `watermarkremover` CLI is now installable as a
      **first-class `dotnet` global tool** with one command:
      `dotnet tool install -g watermarkremover` lands a
      `watermarkremover` binary on `$PATH` (the install location
      follows the standard .NET global-tool path — `~/.dotnet/tools/`
      on Linux/macOS, `%USERPROFILE%\.dotnet\tools\` on Windows). The
      `WatermarkRemover.CLI` csproj is now `IsPackable=true` +
      `PackAsTool=true` + `ToolCommandName=watermarkremover` +
      `PackageId=watermarkremover` + `PackageOutputType=Exe`; the
      `tools/net10.0/any/DotnetToolSettings.xml` that the SDK
      generates maps the command to `watermarkremover.dll` with
      `Runner=dotnet`. The Astro web UI is intentionally **kept in
      the package** (~29 KB on disk) so `watermarkremover serve`
      finds `wwwroot/` at `AppContext.BaseDirectory` after a global
      install — users on headless boxes can pass `--no-ui` to skip
      the file provider. A new `src/WatermarkRemover.CLI/README.md`
      is the per-tool landing page (install + update + uninstall
      + the most-used `clean-*` / `serve` / `serve-mcp` commands +
      links to the alternative install paths and the main repo);
      the shared 128×128 NuGet icon is bundled via an explicit
      `<None Pack="true" PackagePath="\">` item, same shape as the
      four library packages. **20 new xUnit tests** in
      `WatermarkRemover.CLI.Tests/DotnetToolPackagingTests` (csproj
      static-structural + per-`dotnet pack` dynamic-nuspec /
      `DotnetToolSettings.xml` / entry-point-assembly presence /
      package-size sanity) — the test class runs `dotnet pack` once
      per test class (memoised in a `Lazy<string>` so xUnit's
      parallel test scheduler does not trigger duplicate packs) and
      writes the artifact to `out/tool-pack/` so it is easy to
      inspect by hand. The README gains a new "5. As a `dotnet
      tool`" install path under `## 📦 Installation`, parallel to
      the existing pre-built binary / Docker / source / library
      paths. Build clean (0 warnings, 0 errors), **497 xUnit
      tests** (81 + 35 + 26 + 9 + 39 + 307) + 0 Node tests,
      all green; `dotnet pack` of the CLI project produces a
      113 MB `watermarkremover.1.0.0.nupkg` whose `.nuspec` has
      `<packageType name="DotnetTool" />`,
      `<id>watermarkremover</id>`, the right
      `<frameworkReference name="Microsoft.AspNetCore.App" />`,
      and the right `<readme>` / `<icon>`; the `DotnetToolSettings.xml`
      inside the package has
      `<Command Name="watermarkremover" EntryPoint="watermarkremover.dll" Runner="dotnet" />`.

- [x] **WR-P010 — NuGet packaging for the four library projects
      (`src/WatermarkRemover.{Core,Text,Metadata,Image}.csproj` +
      `src/tools/watermarkremover.snk` + `src/tools/watermarkremover-nuget-icon.png`
      + `scripts/SnkGen/` + 4× `README.md` + `src/tests/WatermarkRemover.CLI.Tests/NuGetPackagingTests.cs`)** —
      the core building blocks are now consumable as separately-versioned
      NuGet packages (`WatermarkRemover.Core 1.0.0`,
      `WatermarkRemover.Text 1.0.0`, `WatermarkRemover.Metadata 1.0.0`,
      `WatermarkRemover.Image 1.0.0`) instead of having to clone the repo.
      Each project gets `IsPackable=true` + `PackageId` + `PackageVersion` +
      `PackageReadmeFile` + `PackageIcon` + `PackageLicenseExpression=MIT`
      + `PackageProjectUrl`, and is `SignAssembly=true` against a new
      `src/tools/watermarkremover.snk` (2048-bit RSA, generated by the
      shipped `scripts/SnkGen` helper because the Windows SDK's
      `sn.exe` is not callable in this environment). A 128×128 PNG
      icon is shared across all four packages; per-project `README.md`
      files describe the public surface and a usage example. The
      `MaskGenerator.BuildMask` / `BuildMaskWithRegions` methods were
      promoted from `internal` to `public` to avoid the chicken-and-egg
      `InternalsVisibleTo` strong-name assembly resolution problem
      (the test assembly referenced by `InternalsVisibleTo` is built
      *after* the library that declares it, so the C# compiler can't
      resolve the public key). **39 new xUnit tests** in
      `WatermarkRemover.CLI.Tests/NuGetPackagingTests` (file presence
      for the .snk and the icon, PNG header magic, 128×128 size, the
      `IsPackable=true` / `PackageId` / `PackageVersion` /
      `PackageReadmeFile=README.md` /
      `PackageIcon=watermarkremover-nuget-icon.png` /
      `PackageLicenseExpression=MIT` / `PackageProjectUrl` (HTTPS) /
      `SignAssembly=true` (against `..\tools\watermarkremover.snk`) /
      bundled `<None Pack="true">` items for both README and icon
      — all [Theory] rows across the 4 library projects, plus 3
      unconditional `.snk` / icon checks). Build clean (0 warnings,
      0 errors), **477 xUnit tests** (81 + 35 + 26 + 9 + 39 + 287),
      all green. `dotnet pack` produces a valid `.nupkg` for each of
      the 4 projects (28 KB → 49 KB).

- [x] **WR-S20 — MCP packaging + Docker for MCP
      (`Dockerfile` + `docker-compose.yml` + `.github/workflows/release.yml`
      + `docs/MCP.md`)** — the MCP story is now first-class in both the
      release pipeline and the container image. `Dockerfile` now
      `EXPOSE`s **both** `5080` (HTTP API + Astro UI) and `5090` (MCP
      Streamable HTTP), the header documents a `serve-mcp --transport
      http` CMD variant alongside the existing `serve` default, and a
      regression-guard comment in the runtime stage explains how to
      override `HEALTHCHECK` when running MCP-only. `docker-compose.yml`
      grew two new top-level entries: an `mcp` long-running service
      that runs `serve-mcp --transport http --host 0.0.0.0 --port 5090`
      with the same hardening posture as the HTTP API (read-only root
      fs, tmpfs `/tmp`, `no-new-privileges`, dedicated `healthcheck`
      probing `http://127.0.0.1:5090/health`); and a `clean` service
      under a `clean` profile for one-shot CLI invocations
      (`docker compose run --rm clean clean-text --input /data/in.txt
      --output /data/out.txt`). The release workflow
      (`.github/workflows/release.yml`) gets a new
      `Smoke-test 'serve-mcp' sub-command` step that runs **after** the
      binary is published and **before** the zip is uploaded: it locates
      the apphost (handling both `watermarkremover` and
      `watermarkremover.exe` per RID), invokes `serve-mcp --help`, and
      asserts the help text mentions both `--transport` and `stdio` /
      `http` — so a regression that accidentally trims the
      `ModelContextProtocol` SDK out of the single-file bundle fails
      the build instead of shipping a broken MCP integration. The MCP
      NuGet packages (`ModelContextProtocol` 2.2.0 and
      `ModelContextProtocol.AspNetCore` 2.2.0) were already pinned in
      `src/Directory.Packages.props`; the new
      `McpDockerPackagingTests` class guards them so a future
      central-management refactor that drops one is caught. `docs/MCP.md
      → Docker` grows three subsections: the original
      `docker run` recipe, a new `Docker Compose` walkthrough
      (`docker compose up mcp`, side-by-side with the API, the `clean`
      profile), and a new `Building the image locally` block. README
      unchanged. **17 new xUnit tests** in
      `WatermarkRemover.CLI.Tests/McpDockerPackagingTests` (file
      presence for `Dockerfile` / `docker-compose.yml` /
      `release.yml` / `Directory.Packages.props` / `docs/MCP.md`;
      structural assertions: `EXPOSE 5080 5090` is parsed by a
      digit-only regex that handles the multi-line and single-line
      shapes; the `mcp:` compose service block is matched; the
      `serve-mcp` + `"http"` + `5090` triplet is asserted in the
      compose body; the MCP healthcheck probes the right port; the
      `watermarkremover` service stays intact; the release workflow
      invokes `serve-mcp --help` and asserts the transport-flag help
      text; the docs section mentions `docker compose` and port
      `5090`). Build clean (0 warnings, 0 errors), **438 xUnit tests
      total** (81 + 35 + 9 + 26 + 39 + 248) + **13 Node tests**, all
      green.

- [x] **WR-S19 — VS Code extension (`vscode/watermark-remover/` +
      `docs/VS-CODE.md`)** — the project now ships a first-party VS
      Code extension pre-wired into the repo. The extension is a thin
      UI client over the `watermarkremover` CLI — it registers three
      commands (`watermarkremover.cleanText` / `cleanFile` /
      `detectText`) with full context-menu integration
      (`editor/context` + `editor/context/contextual` +
      `explorer/context` + `explorer/context/contextual` +
      `commandPalette`), four `contributes.configuration` settings
      (`binaryPath`, `preferMcp`, `statistical`, `showNotifications`),
      and ships the same `skills/` folder so AI agents inside VS Code
      (Continue, Cline, …) learn the tool. The extension source is
      `dependency-free at runtime` (only `node:child_process` and the
      `vscode` module; no bundler, no React, no webpack — single
      `out/extension.js` ≈ 16 KB) and a 16-case Node
      `node --test` suite verifies the static contract. New
      `docs/VS-CODE.md` is the end-to-end reference (3 install paths
      × 3 usage flows × settings × source-mode `dotnet run` recipe ×
      MCP-vs-extension complement × 7-row troubleshooting table).
      `docs/MCP.md → VS Code` is a new sibling install section
      between Continue and the npm package (4 recipes: Marketplace /
      stdio `.vscode/mcp.json` / `dotnet run` source / Streamable
      HTTP). README grows a parallel "VS Code users get a
      first-party extension" callout under `## 🧠 Agent skills` and
      the docs footer adds a `🆚 docs/VS-CODE.md` row. **46 new xUnit
      tests** in `WatermarkRemover.CLI.Tests/VsCodeExtensionTests`:
      directory + 7 file-presence theory rows (`package.json`,
      `tsconfig.json`, `README.md`, `CHANGELOG.md`, `.vscodeignore`,
      `.gitignore`, `src/extension.ts`), `skills/` presence,
      `test/*.test.js` non-empty, 6 skill folders each with
      `SKILL.md` (theory rows), `package.json` valid JSON, all 11
      required top-level fields, name + publisher, SemVer,
      `engines.vscode ^1.85.0` (regex
      `^\^1\.(8[5-9]|9[0-9])\.0$`), `engines.node >=18`,
      activationEvents for all three commands, all three commands in
      `contributes.commands` with non-empty title + `WatermarkRemover`
      category, all four menu slots correctly populated (text
      commands in editor menus, file command in explorer menus), all
      four `configuration` settings, `scripts.build = tsc -p .` +
      `vscode:prepublish = npm run build` + test script uses
      `node --test`, all three devDependencies, `repository.directory
      = "vscode/watermark-remover"`, `tsconfig.json` valid JSON +
      ES2022 + strict + `src/**/*` include, `src/extension.ts`
      registers all three commands and imports `child_process` and
      invokes the three CLI subcommands, the README is substantial
      with the right sections, the CHANGELOG has `Unreleased` +
      `Added`, the `.vscodeignore` excludes `src/` + `tsconfig.json`,
      the master skill `compatibility:` lists `vscode`,
      `docs/MCP.md` has a `### VS Code` section, `docs/VS-CODE.md`
      is present with the right sections, and the parent README
      has the "VS Code users" callout. Build clean (0 warnings,
      0 errors), **437 tests total** (421 xUnit + 16 Node), all green.

- [x] **WR-S18 — Cursor / Continue MCP config + `@watermarkremover/mcp` npm
      package** — Cursor and Continue now have **three** install paths each
      in `docs/MCP.md` (release binary on `$PATH`, the new npm wrapper,
      and `dotnet run` source mode), and the project ships a
      **zero-dependency npm package** at
      `npm/watermarkremover-mcp/`. The package contains: `package.json`
      (name `@watermarkremover/mcp`, `bin: watermarkremover-mcp`,
      `engines.node >= 18`, `os`/`cpu` allowlists, `repository.directory`
      pointing at the npm subfolder, no runtime deps); `index.js` — the
      `bin` entry, a Node shebang + `child_process.spawn` of
      `serve-mcp` with `stdio: 'inherit'` and signal forwarding
      (SIGINT/SIGTERM/SIGHUP); `postinstall.js` — the install hook that
      delegates to `lib/install.js` with the package's own version as
      the expected release tag; `lib/binary.js` — the RID table
      (4 entries: linux-x64/arm64, darwin-x64, win-x64, locked to the
      release workflow matrix), `detectRuntimeId()`, `releaseAssetUrl()`,
      `installedBinaryPath()`, and `resolveBinary()` with the priority
      order `WATERMARKREMOVER_BINARY > bin/ > $PATH > unresolved`;
      `lib/install.js` — the in-tree HTTP downloader (uses Node's
      built-in `http`/`https` so the package has zero deps, follows
      GitHub's 302 → `objects.githubusercontent.com` redirects, capped
      at 5 hops) and a hand-rolled ZIP central-directory reader that
      handles STORED and DEFLATE entries (no `adm-zip` / `yauzl` /
      `node-stream-zip`); `test/binary.test.js` — 13 Node `node:test`
      cases covering RID table, platform detection, asset URL shape,
      installed path per platform, resolution priority chain, the
      flat-vs-nested entry matcher, and a synthetic ZIP round-trip
      against the in-tree extractor; `README.md` (install, postinstall
      contract, supported RIDs, env-var escape hatches,
      `WATERMARKREMOVER_BINARY` source-mode swap) and `.gitignore`
      (`bin/`). The postinstall is **idempotent** (re-runs are
      no-ops when the binary is present), **soft-fails** (network
      errors print a remediation message to stderr and exit 0 so
      `npm install` succeeds), and **opt-out-able** via
      `WR_SKIP_BINARY_DOWNLOAD=1`. `WR_FORCE_BINARY_DOWNLOAD=1`
      forces a re-download. `docs/MCP.md` is rewritten: the Cursor
      and Continue sections each expand from one snippet to three
      (binary / npm / source), a new `### npm package (@watermarkremover/mcp)`
      section sits between Continue and Docker with a host-by-host
      wiring table, two full example snippets, the source-mode swap
      recipe, and the RID matrix; the troubleshooting table gains
      two new rows (npm wrapper exit 127, npm postinstall yellow
      warning). README's `## 🤖 MCP server` section grows a "Cursor
      / Continue users get an npm wrapper" callout with the snippet
      shape; the docs-link footer adds a `📦 npm/watermarkremover-mcp/`
      row. 28 new xUnit tests: `NpmPackageTests` (20 — directory +
      6 file-presence theory rows, valid `package.json` JSON,
      scoped name, SemVer, `bin: watermarkremover-mcp` →
      `index.js`, `postinstall` script, test script, `files`
      allowlist, `engines.node >= 18`, `repository.directory`,
      `index.js` shebang + `serve-mcp` arg + `stdio: 'inherit'`,
      `postinstall.js` delegation, `binary.js` advertises every
      supported RID, `install.js` honours skip/force env vars)
      and `CursorContinueConfigTests` (8 — doc presence, `### Cursor`
      and `### Continue` and `### npm package` headers, the embedded
      Cursor + Continue config JSON blocks parse + register
      `watermarkremover` with the right shape, the dedicated
      npm-section Cursor + Continue `npx` snippets parse + reference
      `@watermarkremover/mcp`). Solution build clean (0 warnings,
      0 errors), **375 tests total** (81 + 35 + 26 + 9 + 39 + 185),
      all green; `node --test` on the npm package's own suite
      reports 13/13 green.

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