# 🤖 MCP server integration

> **One-line summary.** `watermarkremover serve-mcp` exposes the full
> pipeline as [Model Context Protocol](https://modelcontextprotocol.io/)
> tools so any MCP-compatible agent — Claude Code, OpenCode, MiniMax
> Code, Cursor, Continue, … — can call `clean_text`, `clean_markdown`,
> `clean_file`, `clean_image`, `detect_text`, `detect_markdown`,
> `inspect_file`, and `detect_watermark` directly. No shell-out, no
> glue code, no boilerplate.

This page is the single source of truth for **agent developers** wiring
WatermarkRemover into an MCP host, and for **end users** who want to
register the server in their editor. Read the
[architecture](#architecture) section if you only have two minutes; the
[tool reference](#tool-reference) and the [install](#install) recipes
are the other two sections you'll come back to.

---

## Table of contents

- [Architecture](#architecture)
- [Transports](#transports)
  - [stdio](#stdio-default)
  - [Streamable HTTP (stateless)](#streamable-http-stateless)
  - [Legacy SSE](#legacy-sse-not-currently-shipped)
- [Tool reference](#tool-reference)
  - [`clean_text`](#clean_text)
  - [`clean_markdown`](#clean_markdown)
  - [`clean_file`](#clean_file)
  - [`clean_image`](#clean_image)
  - [`detect_text`](#detect_text)
  - [`detect_markdown`](#detect_markdown)
  - [`inspect_file`](#inspect_file)
  - [`detect_watermark`](#detect_watermark)
- [Configuration](#configuration)
  - [`config.yaml` (`mcp:` block)](#configyaml-mcp-block)
  - [CLI flags](#cli-flags)
  - [Resolution order](#resolution-order)
- [Install](#install)
  - [Claude Code](#claude-code)
  - [OpenCode](#opencode)
  - [MiniMax Code](#minimax-code)
  - [Cursor](#cursor)
  - [Continue](#continue)
  - [VS Code](#vs-code)
  - [npm package (`@watermarkremover/mcp`)](#npm-package-watermarkremovermcp)
  - [Docker (Streamable HTTP)](#docker-streamable-http)
  - [Verify the install](#verify-the-install)
- [Troubleshooting](#troubleshooting)
- [SDK reference](#sdk-reference)

---

## Architecture

The MCP server is a **transport-agnostic** class library
([`WatermarkRemover.Mcp`](../src/WatermarkRemover.Mcp/)) that maps the
existing pipeline interfaces to Model Context Protocol tools. The CLI
command [`serve-mcp`](../src/WatermarkRemover.CLI/Commands/ServeMcpCommand.cs)
hosts it in either stdio (local agents, default) or Streamable HTTP
(remote agents, Docker). No new business logic lives in the MCP
project — every tool delegates to the same interfaces the CLI and HTTP
API already use.

```
                       ┌──────────────────────────┐
                       │  Agent (Claude Code,     │
                       │  OpenCode, MiniMax Code,  │
                       │  Cursor, Continue, …)    │
                       └────────────┬─────────────┘
                                    │ MCP / JSON-RPC
                                    ▼
        ┌────────────────────────────────────────────────┐
        │  transport: stdio (default)                     │
        │          | Streamable HTTP (stateless)          │
        │          | Legacy SSE (not currently shipped)   │
        └────────────────────────┬───────────────────────┘
                                 │
                                 ▼
                ┌──────────────────────────────┐
                │  WatermarkRemover.Mcp        │
                │  ─ 8 [McpServerTool] methods │
                │  ─ attribute-based discovery │
                │  ─ DI parameter binding      │
                └─────────────┬────────────────┘
                              │ calls existing pipeline interfaces
        ┌─────────────────────┼─────────────────────────────┐
        ▼                     ▼                             ▼
┌────────────────┐   ┌────────────────┐          ┌────────────────┐
│ ITextCleaning  │   │ IMarkdown      │          │ IFileCleaner-  │
│ Pipeline       │   │ Cleaner        │          │ Router         │
│ (Layer A/B/C)  │   │ (21 toggles)   │          │ (JPEG/PNG/PDF/ │
└────────────────┘   └────────────────┘          │  DOCX/HTML/    │
        ┌────────────────┐                        │  WebP)         │
        │ IImageCleaning-│                        └────────────────┘
        │ Pipeline       │
        │ (LaMa inpaint) │
        └────────────────┘
```

**Key invariants:**

- The `WatermarkRemover.Mcp` assembly is **transport-agnostic**. The
  host (`serve-mcp`) decides whether to bind stdin/stdout or HTTP.
  This means a future host — a VS Code extension, an Electron
  wrapper, a desktop tray app — can reuse the same library without
  touching the tool implementations.
- Every tool is a `[McpServerToolType]`-attributed `static class` with
  one `[McpServerTool]` method. The official
  [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
  scans the assembly via `WithToolsFromAssembly()` and generates the
  JSON Schema 2020-12 for each tool from the C# method signature — the
  agent sees a well-formed `inputSchema` for free.
- Tool dependencies (`ITextCleaningPipeline`, `IMarkdownCleaner`,
  `IFileCleanerRouter`, `IImageCleaningPipeline`, `AppConfig`,
  `ILoggerFactory`) are resolved through the same DI graph the CLI
  builds for the rest of the application. There is no parallel
  instantiation path; there is no second source of truth for
  configuration.

---

## Transports

The server ships with two transports. The third (Legacy SSE) is a
deliberately reserved, **not currently shipped** option — see the
note below.

### stdio (default)

Local-agent integration. The MCP host (`serve-mcp`) is spawned as a
child process and the agent talks JSON-RPC over its stdin/stdout.
This is the right choice for **Claude Code**, **OpenCode**, **MiniMax
Code**, **Cursor**, **Continue**, and any other MCP host that runs the
server in-process.

```
┌────────────┐  spawn  ┌────────────────────────┐  stdin/stdout  ┌────────┐
│  Agent     │ ──────▶ │  watermarkremover      │ ◀─────────────▶│  JSON  │
│  (MCP host)│         │  serve-mcp             │   (JSON-RPC)   │  -RPC  │
└────────────┘         └────────────────────────┘                └────────┘
```

**Critical detail: logging goes to stderr.** Per the
[MCP stdio spec](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports),
**stdout is the JSON-RPC channel**. Any text the host writes to stdout
that isn't a valid `Content-Length`-framed JSON-RPC message will
break the stream and cause the agent to disconnect. The
`serve-mcp --transport stdio` command therefore calls
`AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)` so
**every** log level — Trace, Debug, Information, Warning, Error,
Critical — is routed to stderr, and stdout stays clean.

When you run the server standalone, you'll see a single
`WatermarkRemover MCP server starting (stdio transport).` line on
stderr, then the process waits silently on stdin. That silence is the
correct behaviour: the agent owns the lifecycle, and any noise on
stdout would corrupt the protocol.

### Streamable HTTP (stateless)

Remote-agent integration. The server binds to
`http://{host}:{port}/` (default `0.0.0.0:5090`) and exposes the MCP
endpoint via `app.MapMcp()`. The transport is configured with
`Stateless = true` so each request is a self-contained JSON-RPC
exchange — no session, no SSE stream, no persistent state. The
behaviour is identical to the `serve` HTTP API from an integration
standpoint: a per-IP rate-limit (defaults to `server.rate_limit.*`),
an optional `X-API-Key` middleware, and a `/health` endpoint that is
exempt from the auth and rate-limit checks.

```
┌────────────┐   HTTP/JSON-RPC    ┌────────────────────────┐
│  Remote    │ ──────────────────▶│  watermarkremover      │
│  agent     │                    │  serve-mcp             │
│            │ ◀──────────────────│  --transport http      │
└────────────┘                    │  --port 5090           │
                                  └────────────────────────┘
```

This transport is the right choice for **Docker deployments** and
**multi-user / multi-agent scenarios** where the agent is on a
different machine than the server. It's also the only transport where
`--api-key`, `--rate-limit`, and `--rate-window` do anything —
stdio ignores all of them by design (the stdio pipe is the auth
boundary).

### Legacy SSE (not currently shipped)

The Model Context Protocol defined a [legacy HTTP+SSE transport](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
that predates Streamable HTTP. Some older clients (early Claude Desktop
builds, some custom MCP hosts) still speak only the SSE shape. The
`ModelContextProtocol` SDK has an `EnableLegacySse` switch for this
case, but the current `serve-mcp` CLI does **not** expose it — the
two shipped transports are stdio and Streamable HTTP (stateless). If
you hit a client that requires legacy SSE, open an issue and we'll
add a `--transport sse` flag; the wiring is two lines on top of
`WithHttpTransport`.

---

## Tool reference

Eight tools, one per pipeline surface. Every tool's parameter schema
is generated by the SDK from the C# method signature and the
`[Description]` attributes; the descriptions in this section are
copied verbatim from the source so what's documented here is what the
agent actually sees in `tools/list`.

> **Output-shape conventions:**
> - **Text** and **detection** tools return one `TextContentBlock`
>   whose `text` is either the cleaned string or a JSON array of
>   detection records.
> - **`clean_file`** returns **two blocks**: a `TextContentBlock`
>   with a JSON summary (paths, sizes, removed-entry count) **plus**
>   an `EmbeddedResourceBlock` carrying the cleaned bytes as a
>   base64-encoded `BlobResourceContents` with the right MIME type.
>   The agent can either forward the resource to its own caller or
>   decode and write it back to disk.
> - **`clean_image`** always re-encodes to PNG and returns the
>   cleaned bytes as an `ImageContentBlock` so the agent can display
>   the result directly without a base64 decode.
> - **Tool errors** (bad input, missing file, unsupported format)
>   surface as `CallToolResult.IsError = true` with a human-readable
>   message in a `TextContentBlock` — *not* as a protocol-level
>   error, per the MCP spec's distinction between tool errors and
>   transport errors.

### `clean_text`

Strip invisible characters, vendor watermarks, and (optionally)
green-list statistical rewrites from a text payload.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` *(required)* | `string` | — | The text payload to clean. May be a snippet, a paragraph, or a multi-kilobyte document. |
| `statistical` | `bool` | `false` | When `true`, enable Layer B (statistical / green-list rewriting). Off by default to keep changes minimal. |
| `no_unicode` | `bool` | `false` | When `true`, disable Layer A (Unicode hygiene). Off by default; leave on unless you have a reason. |
| `no_vendor` | `bool` | `false` | When `true`, disable Layer C (vendor-specific detectors). Off by default. |
| `include_removed_summary` | `bool` | `false` | When `true`, append a JSON summary of the removed items and detections to the response. Useful for debug / audit workflows. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "clean_text",
    "arguments": {
      "text": "Hello\u200BWorld",
      "include_removed_summary": true
    }
  }
}
```

**Response:**

```json
{
  "content": [
    { "type": "text", "text": "HelloWorld" },
    {
      "type": "text",
      "text": "{\"removedItems\":[...],\"detections\":[],\"confidence\":0.97}"
    }
  ],
  "isError": false
}
```

### `clean_markdown`

Strip AI-specific markdown artefacts (frontmatter, signatures,
mentions, link / image watermarks, optionally headings / fences /
links) while preserving fenced code blocks.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `markdown` *(required)* | `string` | — | The markdown document to clean. Fenced code blocks are preserved. |
| `strip_code_fences` | `bool` | `false` | When `true`, also strip code fences and their content. Off by default. |
| `strip_headings` | `bool` | `false` | When `true`, also strip `#`, `##`, … headings. Off by default. |
| `strip_links` | `bool` | `false` | When `true`, also strip `[text](url)` links and `![alt](src)` images. Off by default. |
| `include_removed_summary` | `bool` | `false` | When `true`, append a JSON summary of removed items alongside the cleaned markdown. |

The cleaner also reads the 21-togggle `markdown:` block from
`config.yaml` as the baseline; the per-call flags here are
**overrides on top of the config baseline** (logical OR, so passing
`strip_code_fences=true` does not turn it off elsewhere).

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "clean_markdown",
    "arguments": {
      "markdown": "---\ntitle: Test\n---\n# Hello\nWorld\n"
    }
  }
}
```

**Response:**

```json
{
  "content": [
    { "type": "text", "text": "# Hello\nWorld\n" }
  ],
  "isError": false
}
```

### `clean_file`

Strip metadata (EXIF, XMP, IPTC, C2PA, etc.) from a file on disk and
return the cleaned bytes as a resource block the agent can hand back
to its caller or write elsewhere.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `input_path` *(required)* | `string` | — | Absolute path to the file to clean. JPEG / PNG / WebP / PDF / DOCX / HTML. |
| `output_directory` | `string?` | next to the input | Override directory for the cleaned output. When omitted, the cleaned file is written next to the input with a `.cleaned` suffix. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "clean_file",
    "arguments": {
      "input_path": "/tmp/photo.jpg"
    }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "{\"inputPath\":\"/tmp/photo.jpg\",\"outputPath\":\"/tmp/photo.cleaned.jpg\",\"removedEntries\":17,\"inputSizeBytes\":3492324,\"outputSizeBytes\":3320001,\"processingTimeMs\":42,\"mimeType\":\"image/jpeg\"}"
    },
    {
      "type": "resource",
      "resource": {
        "uri": "file:///tmp/photo.cleaned.jpg",
        "mimeType": "image/jpeg",
        "blob": "<base64-encoded cleaned bytes>"
      }
    }
  ],
  "isError": false
}
```

### `clean_image`

Remove visual watermarks from an image via the full
mask → resize → ONNX inpaint → blend pipeline. Returns the cleaned
PNG as an `ImageContentBlock` the agent can display directly.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `input_path` *(required)* | `string` | — | Absolute path to the image (JPEG / PNG / WebP). |
| `mask_path` | `string?` | auto-detect | Pre-built mask PNG (grayscale, white = inpaint). Omit to auto-detect. |
| `threshold` | `double?` | `0.4` (config) | Auto-detection confidence threshold in `[0, 1]`. Higher = fewer regions flagged. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "clean_image",
    "arguments": {
      "input_path": "/tmp/photo.png",
      "threshold": 0.6
    }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "{\"inputPath\":\"/tmp/photo.png\",\"outputPath\":\"/tmp/photo.cleaned.png\",\"detectedRegions\":1,\"inputSize\":{\"width\":1920,\"height\":1080},\"outputSize\":{\"width\":1920,\"height\":1080},\"processingTimeMs\":2300,\"modelUsed\":\"big_lama_regular_inpaint.onnx\"}"
    },
    {
      "type": "image",
      "mimeType": "image/png",
      "data": "<base64-encoded PNG bytes>"
    }
  ],
  "isError": false
}
```

> **Graceful degradation:** if the ONNX model is missing, the tool
> returns the **unchanged** image with `modelUsed: "none"` in the
> summary. Run `watermarkremover download-model` to enable visual
> watermark removal.

### `detect_text`

Detect (without removing) AI vendor watermark signatures in a text
payload. Useful for audit / inspection workflows.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` *(required)* | `string` | — | The text payload to inspect. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "detect_text",
    "arguments": { "text": "H\u0435llo\u200B\u200BWorld" }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "[{\"Vendor\":\"Claude\",\"Pattern\":\"homoglyph\",\"Position\":1,\"Length\":1,\"Confidence\":0.92},{\"Vendor\":\"Claude\",\"Pattern\":\"zeroWidthRun\",\"Position\":5,\"Length\":2,\"Confidence\":0.88}]"
    }
  ],
  "isError": false
}
```

### `detect_markdown`

Detect (without removing) AI artefacts in a markdown document:
frontmatter, vendor signatures, mentions, link / image patterns.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `markdown` *(required)* | `string` | — | The markdown document to inspect. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "detect_markdown",
    "arguments": { "markdown": "---\ntitle: Test\n---\n# Hello\n" }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "[{\"Type\":\"frontmatter\",\"Description\":\"YAML frontmatter block\",\"Line\":1,\"Column\":1}]"
    }
  ],
  "isError": false
}
```

### `inspect_file`

Report every metadata entry in a file — without modifying it. Use
this before `clean_file` if you want to confirm what is about to be
stripped.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `input_path` *(required)* | `string` | — | Absolute path to the file to inspect. JPEG / PNG / WebP / PDF / DOCX / HTML. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "inspect_file",
    "arguments": { "input_path": "/tmp/photo.png" }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "[{\"Container\":\"XMP/Text\",\"Key\":\"tEXt\",\"Value\":\"Software\\u0000Test\"},{\"Container\":\"EXIF\",\"Key\":\"Make\",\"Value\":\"Acme\"}]"
    }
  ],
  "isError": false
}
```

### `detect_watermark`

Detect (without inpainting) visual watermark regions in an image.
Useful for "is there a watermark, and where?" workflows that should
not mutate the source file.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `input_path` *(required)* | `string` | — | Absolute path to the image. |
| `threshold` | `double?` | `0.4` (config) | Auto-detection confidence threshold in `[0, 1]`. |

**Request:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "detect_watermark",
    "arguments": { "input_path": "/tmp/photo.png" }
  }
}
```

**Response:**

```json
{
  "content": [
    {
      "type": "text",
      "text": "[{\"X\":12,\"Y\":340,\"Width\":80,\"Height\":24,\"Confidence\":0.87}]"
    }
  ],
  "isError": false
}
```

---

## Configuration

The MCP server reads its settings from the `mcp:` block in
`src/config.yaml`, layered with CLI-flag overrides at start-up. The
same precedence as the rest of the application: **CLI > config.yaml >
built-in defaults**.

### `config.yaml` (`mcp:` block)

```yaml
mcp:
  # Transport selection.
  #   - "stdio"  (default) — local stdio pipe for Claude Code / OpenCode
  #                          / MiniMax Code / Cursor / Continue.
  #   - "http"              — Streamable HTTP, stateless, for remote
  #                          agents and Docker.
  transport: "stdio"

  # Interface / port for the HTTP transport. Ignored for stdio.
  # Port 5090 is distinct from `serve`'s 5080 so the two commands can
  # run side by side without flag-flipping.
  host: "0.0.0.0"
  port: 5090

  # API key for the HTTP transport. When set, every request must carry
  # the matching `X-API-Key` header — same auth pattern as `serve`.
  # Leave `null` (or unset) for localhost dev. Ignored for stdio —
  # the stdio pipe is the auth boundary.
  api_key: null

  # Per-IP rate-limit for the HTTP transport. When omitted, inherits
  # the `server.rate_limit` block above. Ignored for stdio.
  rate_limit:
    permit_limit: 100    # requests per window, per IP
    window_seconds: 60   # window length
    queue_limit: 0       # 0 = reject immediately when the limit is hit
```

### CLI flags

| Flag | Default | Description |
|------|---------|-------------|
| `--transport <stdio\|http>` | `stdio` | Transport to bind. |
| `-H`, `--host <HOST>` | `0.0.0.0` | Interface for the HTTP transport. Ignored for stdio. |
| `-p`, `--port <PORT>` | `5090` | TCP port for the HTTP transport. Ignored for stdio. |
| `--api-key <KEY>` | _(none)_ | Require this key via the `X-API-Key` header. Ignored for stdio. |
| `--rate-limit <REQUESTS>` | `100` (or `mcp.rate_limit.permit_limit`) | Override the per-IP rate-limit. Ignored for stdio. |
| `--rate-window <SECONDS>` | `60` (or `mcp.rate_limit.window_seconds`) | Override the rate-limit window length. Ignored for stdio. |

### Resolution order

For every knob, the resolution order is:

1. **CLI flag** (e.g. `--port 6000`) — wins.
2. **`mcp:` block in `config.yaml`** — wins when the CLI flag is absent.
3. **`server.rate_limit` block** — used as a fallback for rate-limit
   settings when the `mcp.rate_limit` block is absent (MCP inherits
   the HTTP server's defaults).
4. **Built-in defaults** baked into the
   [`McpConfig`](../src/WatermarkRemover.Core/Configuration/AppConfig.cs)
   class.

Unknown values fail fast: e.g. `--transport sse` returns
`Unknown MCP transport 'sse'. Supported: stdio, http.` and exits
with code `1` rather than silently falling back to stdio.

---

## Install

The one-liner recipes below register the MCP server with each host.
Replace the path / image reference with the one that matches your
install (release binary, `dotnet run`, or Docker image).

### Claude Code

```bash
# From a release binary on $PATH (recommended)
claude mcp add watermarkremover -- watermarkremover serve-mcp

# From source (one-off, while iterating)
claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp
```

Verify with `claude mcp list` — the `watermarkremover` row should
show `connected` once the agent starts.

### OpenCode

OpenCode's actual discovery model is different from a flat
`mcp-config.json`. **MCP servers** are registered in
`opencode.jsonc` (project) or `~/.config/opencode/opencode.json`
(global) under the `mcp` key. **Skills** live as
`<name>/SKILL.md` folders under `.opencode/skills/`. **Slash
commands** are individual `<name>.md` files under
`.opencode/commands/` — they auto-appear in the TUI as
`/<filename>`.

This project ships the skill + the three slash commands pre-wired
under `.opencode/`, so the agent learns the integration as soon as
you open the repo. You only need to:

1. Install the `watermarkremover` binary (see
   [README → Installation](../README.md#-installation)).
2. Flip the MCP entry to `enabled: true` in `.opencode/opencode.jsonc`.

Concretely, the project's `.opencode/opencode.jsonc` contains a
`watermarkremover` MCP entry (commented explanation inline):

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "watermarkremover": {
      "type": "local",
      "command": "watermarkremover",
      "args": ["serve-mcp"],
      "enabled": false   // flip to true after installing the binary
    }
  }
}
```

> **Why `enabled: false` by default?** The `watermarkremover` binary
> may not be on `$PATH` for every contributor. Keeping it disabled
> means a fresh clone boots cleanly; once the binary is installed,
> flip the flag and restart OpenCode.

**Skills.** The project ships
`.opencode/skills/watermark-remover/SKILL.md` — the master skill
that teaches the agent when to call the tools. The five per-format
skills (`watermark-clean-text`, `watermark-clean-markdown`,
`watermark-clean-file`, `watermark-clean-image`, `watermark-detect`)
live under [`skills/`](../skills/) and are auto-discovered by
OpenCode when copied into `.opencode/skills/`. The fastest way to
install them on a *different* project is:

```bash
./skills/install.sh --agent opencode
# or, on Windows
./skills/install.ps1 -Agent opencode
```

**Slash commands.** The project ships three commands under
`.opencode/commands/`:

- `/wr-clean-text <text>` — strip invisible chars + AI watermarks
- `/wr-clean-file <path>` — strip metadata from a file
- `/wr-detect <text>` — detect watermarks without modifying

OpenCode auto-discovers them; they appear in the TUI slash-command
picker without any extra wiring.

**For a different project** (not this one), copy the same
artifacts into your own `.opencode/` directory:

```bash
mkdir -p .opencode/skills .opencode/commands

# Master skill
cp -R ../ai-watermark-remover/.opencode/skills/watermark-remover \
      .opencode/skills/

# Per-format skills (the installer above does all five at once)
./skills/install.sh --agent opencode --target .opencode/skills

# Slash commands
cp ../ai-watermark-remover/.opencode/commands/wr-*.md .opencode/commands/
```

Then add the `watermarkremover` block to that project's
`opencode.jsonc` (or `~/.config/opencode/opencode.json` for a
global install).

**Source-mode install.** If you don't have a release binary and
want OpenCode to build on demand, swap the `command` + `args` for:

```jsonc
"command": "dotnet",
"args": [
  "run",
  "--project",
  "/absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI",
  "--",
  "serve-mcp"
]
```

The first invocation will be slow (build) but subsequent ones are
fast.

### MiniMax Code

The project ships a **V1 local plugin package** under
[`minimax-code/watermark-remover/`](../minimax-code/watermark-remover/).
Copy the folder into MiniMax Code's plugin directory (e.g.
`~/.local/share/MiniMax/plugins/watermark-remover/` on Linux) and
toggle the plugin on in the Plugins pane. The MCP server entry in
[`servers.mcp.json`](../minimax-code/watermark-remover/servers.mcp.json)
is a stdio child process — `watermarkremover serve-mcp` — and the
six skills under `skills/` are auto-discovered.

For the full reference (plugin layout, transport notes, troubleshooting,
slash-command status), see
[`docs/MINIMAX-CODE.md`](./MINIMAX-CODE.md).

For a remote / multi-user setup, swap the stdio transport for
Streamable HTTP and run the server on the same host:

```bash
# Edit minimax-code/watermark-remover/servers.mcp.json to:
#   { "watermarkremover": { "type": "streamable-http", "url": "http://localhost:5090/" } }
watermarkremover serve-mcp --transport http --port 5090
```

### Cursor

Cursor reads MCP servers from `~/.cursor/mcp.json` (a project-local
`.cursor/mcp.json` also works and overrides the user-global one —
useful when you want the integration in just one repo). The config
shape is an **object** keyed by server name, with `command` + `args`
matching the stdio MCP spec.

**A. Release binary (recommended when `watermarkremover` is on `$PATH`):**

Create or edit `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "watermarkremover": {
      "command": "watermarkremover",
      "args": ["serve-mcp"]
    }
  }
}
```

**B. npm wrapper (no manual install — `npx` fetches the binary):**

```json
{
  "mcpServers": {
    "watermarkremover": {
      "command": "npx",
      "args": ["-y", "@watermarkremover/mcp"]
    }
  }
}
```

**C. Source mode (from a `git clone` checkout):**

```json
{
  "mcpServers": {
    "watermarkremover": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI",
        "--",
        "serve-mcp"
      ]
    }
  }
}
```

Restart Cursor — the `WatermarkRemover` server and its eight tools
will show up in the MCP tool picker. If you see the server listed
but no tools, check the Cursor MCP panel's stderr pane — the
`serve-mcp` host prints every log level to stderr per the MCP stdio
contract.

### Continue

Continue reads MCP servers from `~/.continue/config.json` (the
project-local `.continue/config.json` is the same shape and
overrides the user-global one). The config shape is an **array** of
objects, each with its own `name` field — slightly different from
Cursor's object-keyed map.

**A. Release binary (recommended when `watermarkremover` is on `$PATH`):**

Create or edit `~/.continue/config.json` and add an `mcpServers`
array:

```json
{
  "mcpServers": [
    {
      "name": "watermarkremover",
      "command": "watermarkremover",
      "args": ["serve-mcp"]
    }
  ]
}
```

**B. npm wrapper (no manual install — `npx` fetches the binary):**

```json
{
  "mcpServers": [
    {
      "name": "watermarkremover",
      "command": "npx",
      "args": ["-y", "@watermarkremover/mcp"]
    }
  ]
}
```

**C. Source mode (from a `git clone` checkout):**

```json
{
  "mcpServers": [
    {
      "name": "watermarkremover",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI",
        "--",
        "serve-mcp"
      ]
    }
  ]
}
```

Reload the Continue window — the `watermarkremover` server should
appear in the tool sidebar with all eight tools.

### VS Code

VS Code (≥ 1.86) has built-in MCP support that reads server entries
from `.vscode/mcp.json` (project-local) or the user-level **MCP:
Manage Servers** panel. The project also ships a **first-party VS
Code extension** at
[`vscode/watermark-remover/`](../vscode/watermark-remover/) that adds
three commands (right-click a selection → **Clean AI watermarks**,
right-click a file → **Strip metadata**, command palette → **Detect
AI watermarks**) on top of the CLI. The extension is the right
choice for human-driven operations; the MCP registration is the
right choice for AI agents (Continue, Cline, …) running inside
VS Code.

**A. Install the extension (recommended for human-driven use):**

1. Open VS Code → **Extensions** sidebar → search for
   **WatermarkRemover** (publisher `techbuzzz`) → **Install**.

2. Make sure the `watermarkremover` binary is on `$PATH` (or set
   `watermarkremover.binaryPath` in **Settings** to the explicit
   path).

3. Reload VS Code. Right-click any text selection → **WatermarkRemover:
   Clean AI watermarks from selection** appears in the context menu;
   right-click any file in the Explorer → **WatermarkRemover: Strip
   metadata from selected file(s)**.

**B. Register the MCP server (recommended for AI agents):**

Create or edit `.vscode/mcp.json` in your project root:

```json
{
  "servers": {
    "watermarkremover": {
      "type": "stdio",
      "command": "watermarkremover",
      "args": ["serve-mcp"]
    }
  }
}
```

Or for a user-global registration, open the **MCP: Manage Servers**
panel via the command palette and add the same `command` / `args`
pair.

**C. Source mode (from a `git clone` checkout):**

```json
{
  "servers": {
    "watermarkremover": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI",
        "--",
        "serve-mcp"
      ]
    }
  }
}
```

**D. Streamable HTTP (Docker, remote):**

```json
{
  "servers": {
    "watermarkremover": {
      "type": "http",
      "url": "http://localhost:5090"
    }
  }
}
```

After any of the above, restart VS Code (or run **MCP: Restart
Server** from the command palette). The eight tools appear in the
agent-tool picker; the bundled extension commands are independent
and stay available whether or not the MCP server is connected. See
[`docs/VS-CODE.md`](../docs/VS-CODE.md) for the full extension
reference, configuration knobs, and the source-mode
`watermarkremover.binaryPath` recipe.

### npm package (`@watermarkremover/mcp`)

The project also ships a **zero-dependency npm wrapper** for hosts
that prefer npm-based MCP registration. The package
[`@watermarkremover/mcp`](../npm/watermarkremover-mcp/) downloads the
platform-appropriate release artefact on `npm install` and exposes a
`watermarkremover-mcp` shim that spawns `watermarkremover serve-mcp`
with stdio inheritance.

```bash
# install into a project
npm install @watermarkremover/mcp

# or one-off (no install) — useful for quick smoke tests
npx -y @watermarkremover/mcp
```

The package is **thin by design**: no `node-fetch`, no `adm-zip`, no
runtime dependencies at all. The downloader and the in-tree ZIP
extractor both rely on Node built-ins (`https`, `node:zlib`,
`node:fs/promises`). The full source of truth lives at
[`npm/watermarkremover-mcp/`](../npm/watermarkremover-mcp/); the
package's own `README.md` documents the RID matrix, the postinstall
contract, the env-var escape hatches, and the `WATERMARKREMOVER_BINARY`
source-mode swap.

**Wire it into any MCP host:**

| Host      | Config file                       | `mcpServers` shape |
|-----------|-----------------------------------|--------------------|
| Cursor    | `~/.cursor/mcp.json`              | Object keyed by server name |
| Continue  | `~/.continue/config.json`         | Array of `{name, command, args}` objects |
| Claude Code | `claude mcp add …` (one-liner) | n/a — CLI |
| OpenCode  | `.opencode/opencode.jsonc` → `mcp` | Object keyed by server name |

The Cursor and Continue snippets are in the [Cursor](#cursor) and
[Continue](#continue) sections above. The snippet shape is the same
as the release-binary path with one difference — the `command` is
`npx` and the `args` include `-y @watermarkremover/mcp`:

```json
{
  "mcpServers": {
    "watermarkremover": {
      "command": "npx",
      "args": ["-y", "@watermarkremover/mcp"]
    }
  }
}
```

```json
{
  "mcpServers": [
    {
      "name": "watermarkremover",
      "command": "npx",
      "args": ["-y", "@watermarkremover/mcp"]
    }
  ]
}
```

**Source-mode install** (skip the binary download and point at a
local build):

```bash
export WR_SKIP_BINARY_DOWNLOAD=1
export WATERMARKREMOVER_BINARY=/absolute/path/to/watermarkremover
npm install @watermarkremover/mcp
```

The wrapper will prefer `$WATERMARKREMOVER_BINARY` when it resolves
the binary to spawn, so the package installs cleanly even on a
checkout that has never downloaded a release artefact.

### Docker (Streamable HTTP)

The release Docker image (`techbuzzz/watermarkremover:latest`) ships
`dotnet` and the `serve-mcp` command together. Expose the MCP HTTP
endpoint and connect from any client that speaks Streamable HTTP:

```bash
docker run --rm -p 5090:5090 \
  -e WATERMARKREMOVER__MCP__API_KEY=s3cret \
  techbuzzz/watermarkremover:latest \
  serve-mcp --transport http --port 5090 --api-key s3cret
```

The agent's MCP client config then points at
`http://localhost:5090/` (or the Docker host's IP) with the matching
`X-API-Key` header on every request.

> **Environment-variable overrides** work too: the layered config
> reads `WATERMARKREMOVER__MCP__TRANSPORT`, `WATERMARKREMOVER__MCP__PORT`,
> `WATERMARKREMOVER__MCP__API_KEY`, `WATERMARKREMOVER__MCP__HOST`, and
> the rate-limit sub-keys the same way ASP.NET Core's
> `EnvironmentVariablesConfigurationProvider` does. Double-underscore
> (`__`) is the section separator.

### Verify the install

Once the server is registered, ask the agent to list the available
tools. The agent should report eight tools, all prefixed
`clean_…` / `detect_…` / `inspect_…`. If any are missing, see the
[troubleshooting](#troubleshooting) section.

A quick smoke test that requires no agent is the `tools/list` JSON-RPC
request directly against an HTTP server:

```bash
curl -X POST http://localhost:5090/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/list"
  }'
```

The response should enumerate all eight tools. The
`tools/call` happy path (with ZWSP) is:

```bash
curl -X POST http://localhost:5090/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "clean_text",
      "arguments": { "text": "Hello\u200BWorld" }
    }
  }'
```

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `Unknown MCP transport 'sse'` | Legacy SSE is not currently shipped. | Use `stdio` or `http`. Open an issue if you need SSE. |
| Agent disconnects immediately on stdio | Something is writing to stdout. | Run the server standalone: any non-empty stdout that is not a valid JSON-RPC frame breaks the stream. Logs are routed to stderr already; check your wrapper. |
| `File not found: …` on `clean_file` / `inspect_file` / `clean_image` | The agent's working directory is not what you think. | Pass an absolute `input_path` — the tools do not resolve relative paths against the agent's CWD. |
| `Unsupported file type: .ext` | The router only handles JPEG / PNG / WebP / PDF / DOCX / HTML. | For other formats, use a dedicated tool or wait for the metadata roadmap items (TIFF, HEIC, AVIF, …). |
| `modelUsed: "none"` in `clean_image` summary | The ONNX inpainting model is missing. | Run `watermarkremover download-model` to fetch the LaMa weights. The image is returned unchanged until then. |
| HTTP 401 on every request | `--api-key` is set on the server, the agent does not send `X-API-Key`. | Either unset `--api-key` (localhost dev) or configure the agent to send the header. |
| HTTP 429 after a few requests | Per-IP rate-limit is in effect. | Raise `mcp.rate_limit.permit_limit` in `config.yaml` or pass `--rate-limit N` on the CLI. |
| `Port 5090 is already in use` | Another process is bound to the port. | Pass `--port <other>` or set `mcp.port` in `config.yaml`. |
| `@watermarkremover/mcp: failed to locate the WatermarkRemover binary` (exit 127) | The postinstall download didn't complete, the user opted out, or the binary isn't on `$PATH`. | Re-run `npm install @watermarkremover/mcp`, set `WATERMARKREMOVER_BINARY` to a local build, or install the release binary on `$PATH`. |
| npm postinstall prints a yellow warning | The release artefact download failed (network, GitHub rate-limit, unsupported RID). | Re-run `npm install` later, set `WR_FORCE_BINARY_DOWNLOAD=1` to retry, or `WR_SKIP_BINARY_DOWNLOAD=1` and point `WATERMARKREMOVER_BINARY` at a local build. |

---

## SDK reference

The MCP server is built on the official
[`ModelContextProtocol` C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
(maintained in collaboration with Microsoft, NuGet version pinned in
[`Directory.Packages.props`](../src/Directory.Packages.props)). The
relevant entry points:

- **[Server / transport](https://csharp.sdk.modelcontextprotocol.io/)** —
  `AddMcpServer`, `WithStdioServerTransport`, `WithHttpTransport`,
  `WithToolsFromAssembly`. The library in this repo adds a single
  extension method, `AddWatermarkRemoverMcp`, that wires all three
  together with the shared `ServerInfo` metadata.
- **[Tool attributes](https://github.com/modelcontextprotocol/csharp-sdk#server)** —
  `[McpServerToolType]` on the class, `[McpServerTool]` on the method,
  `[Description]` on the parameters. The SDK auto-generates the JSON
  Schema 2020-12 for each tool from the C# method signature.
- **[Content types](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)** —
  `TextContentBlock`, `ImageContentBlock`, `EmbeddedResourceBlock`
  with `BlobResourceContents`. The library uses each of these
  deliberately: `TextContentBlock` for text and detection results,
  `EmbeddedResourceBlock` for `clean_file`'s cleaned bytes, and
  `ImageContentBlock` for `clean_image`'s cleaned PNG.
- **[Transports spec](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)** —
  the canonical description of stdio, Streamable HTTP, and (legacy)
  HTTP+SSE. The `serve-mcp` CLI exposes the first two; the third is
  not currently shipped.
