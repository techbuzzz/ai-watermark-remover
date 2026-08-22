# 🪝 MiniMax Code integration

> **One-line summary.** Copy the `minimax-code/watermark-remover/`
> folder from this repo into MiniMax Code's plugin directory, enable
> the plugin, and the agent will learn how to strip AI watermarks from
> text, markdown, documents, and images through 8 MCP tools and 6
> reusable skills.

This document is the **end-to-end reference** for installing the
WatermarkRemover plugin in MiniMax Code. It mirrors the
[`docs/CLAUDE-CODE.md`](./CLAUDE-CODE.md) and
[`docs/MCP.md`](./MCP.md) install recipes, with everything that's
specific to MiniMax Code's plugin runtime.

---

## Table of contents

- [What's in the package](#whats-in-the-package)
- [Install](#install)
  - [Quick install](#quick-install)
  - [Verify the install](#verify-the-install)
- [What the agent sees](#what-the-agent-sees)
- [Slash commands (forward-looking)](#slash-commands-forward-looking)
- [MCP transport notes](#mcp-transport-notes)
- [CLI fallback](#cli-fallback)
- [Troubleshooting](#troubleshooting)
- [Assumptions and open questions](#assumptions-and-open-questions)
- [Reference](#reference)

---

## What's in the package

The MiniMax Code plugin lives at the repo root under
[`minimax-code/watermark-remover/`](../minimax-code/watermark-remover/).
It is a self-contained V1 plugin package — the same shape the MiniMax
Code Desktop runtime expects for local plugins:

```text
minimax-code/watermark-remover/
├── .minimax-plugin/
│   └── plugin.json              # V1 manifest (required)
├── icon.png                     # Plugin icon
├── servers.mcp.json             # stdio MCP server config
├── skills/
│   ├── watermark-remover/SKILL.md       # master skill
│   ├── watermark-clean-text/SKILL.md
│   ├── watermark-clean-markdown/SKILL.md
│   ├── watermark-clean-file/SKILL.md
│   ├── watermark-clean-image/SKILL.md
│   └── watermark-detect/SKILL.md
├── commands/                    # forward-looking slash commands
│   ├── wr-clean-text.md
│   ├── wr-clean-file.md
│   └── wr-detect.md
└── README.md                    # plugin-level install notes
```

The plugin is a **drop-in folder** — no `npm install`, no
`dotnet build`, no special installer. Copy it into the right
directory and MiniMax Code picks it up on the next rescan.

The V1 plugin spec mandates that the manifest directory and the
plugin name match — that's why the per-format skills are named
`watermark-clean-text` / `watermark-clean-markdown` /
`watermark-clean-file` / `watermark-clean-image` / `watermark-detect`
(consistent with the shared `skills/` folder in this repo) rather
than the shorter `clean-text` etc. that the OpenCode / Claude Code
skills use. The skill *content* is otherwise identical.

---

## Install

### Quick install

1. **Install the `watermarkremover` binary.** See
   [README → Installation](../README.md#-installation). The plugin
   shells out to `watermarkremover serve-mcp` and will fail at startup
   if the binary is not on `$PATH`.

2. **Copy the plugin folder** into the right location for your OS:

   | OS      | Target path                                                       |
   |---------|-------------------------------------------------------------------|
   | Linux   | `~/.local/share/MiniMax/plugins/watermark-remover/`               |
   | macOS   | `~/Library/Application Support/MiniMax/plugins/watermark-remover/` |
   | Windows | `%APPDATA%\MiniMax\plugins\watermark-remover\`                    |

   From the repo root:

   ```bash
   # Linux / macOS
   rm -rf ~/.local/share/MiniMax/plugins/watermark-remover
   cp -R minimax-code/watermark-remover \
         ~/.local/share/MiniMax/plugins/watermark-remover
   ```

   ```powershell
   # Windows PowerShell
   $dest = Join-Path $env:APPDATA 'MiniMax\plugins\watermark-remover'
   if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
   Copy-Item -Recurse -Force minimax-code/watermark-remover $dest
   ```

3. **Enable the plugin** in MiniMax Code's Plugins pane (Settings →
   Plugins → `WatermarkRemover` → toggle on). The MCP server will be
   auto-registered as a stdio child process on the next agent
   launch.

4. **Verify** — see the next section.

> **Source-mode install.** If you don't have a release binary and want
> MiniMax Code to build on demand, edit
> `minimax-code/watermark-remover/servers.mcp.json` and swap the
> `command` + `args` for:
>
> ```json
> {
>   "watermarkremover": {
>     "type": "stdio",
>     "command": "dotnet",
>     "args": [
>       "run",
>       "--project",
>       "/absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI",
>       "--",
>       "serve-mcp"
>     ]
>   }
> }
> ```
>
> The first invocation will be slow (build); subsequent ones are fast.

### Verify the install

Once the plugin is enabled, the simplest way to confirm it is wired
correctly is to ask the agent to list its MCP tools. The agent should
report eight tools, all prefixed with `clean_…`, `detect_…`, or
`inspect_…`:

> `clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
> `detect_text`, `detect_markdown`, `inspect_file`, `detect_watermark`

If the agent reports fewer (or none), open the Plugins pane — a
red/yellow dot on the `WatermarkRemover` row means the MCP server
failed at startup (most often a missing binary on `$PATH`). See the
[Troubleshooting](#troubleshooting) section below.

For a sub-process-level smoke test, run the server in a terminal and
send a JSON-RPC `tools/list` request over stdio:

```bash
watermarkremover serve-mcp
# in a second terminal, pipe a tools/list request via the JSON-RPC
# framing the MCP stdio spec requires (Content-Length headers):
printf 'Content-Length: 76\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' \
  | nc -U /dev/stdin
```

A 200-status JSON response enumerating the 8 tool names is the green
light.

---

## What the agent sees

When the plugin is enabled, the MiniMax Code agent has three new
artifacts to work with:

- **MCP server** — `watermarkremover` — registered as a stdio child
  process, 8 tools, all listed in
  [`docs/MCP.md → Tool reference`](./MCP.md#tool-reference).
- **Six skills** — the master `watermark-remover` skill plus the five
  per-format skills. The YAML frontmatter `description` field on each
  one tells the agent when to activate it; the body of the file
  contains the routing table, the MCP tool call shape, and the CLI
  fallback. See [`docs/SKILLS.md`](./SKILLS.md) for the resolution
  rules.
- **Three slash-command files** under `commands/`
  (`wr-clean-text.md`, `wr-clean-file.md`, `wr-detect.md`) — see
  [the next section](#slash-commands-forward-looking) for the current
  status.

A representative agent interaction:

> **User:** "This text from Claude has weird invisible characters.
> Strip them."
>
> **Agent:** calls `clean_text` with `stripInvisible: true` on the
> pasted text, replies `"Helloworld"` plus a `removed` array naming
> `ZWSP x2`.

See the master skill
[`minimax-code/watermark-remover/skills/watermark-remover/SKILL.md`](../minimax-code/watermark-remover/skills/watermark-remover/SKILL.md)
for the full routing table and the worked examples.

---

## Slash commands (forward-looking)

The V1 plugin manifest format does **not** yet declare `commands` as
a first-class capability — the runtime only knows about
`mcpServers` and `skills`. The `commands/` folder is therefore a
**forward-looking** set of files: if a future MiniMax Code version
auto-discovers `commands/<name>.md` (the same convention OpenCode
uses), they will surface as `/wr-clean-text`, `/wr-clean-file`, and
`/wr-detect` slash commands without any further wiring.

Until then, the agent invokes the underlying MCP tools directly
through the routing table in the master skill — i.e. there is no
"this slash command is missing" error path; the same intent is
expressed as a `clean_text` / `clean_file` / `detect_text` tool call.

| Slash command       | Equivalent MCP tool call                             |
|---------------------|------------------------------------------------------|
| `/wr-clean-text`    | `clean_text` with `stripInvisible: true`             |
| `/wr-clean-file`    | `clean_file` with the file path                      |
| `/wr-detect`        | `detect_text` with the pasted text                   |

If you want to wire the slash commands today, the
[`create-agent`](../README.md) and agent customisation surfaces
in MiniMax Code let you bind command names to MCP tool calls;
see the MiniMax Code user guide for the current shape of that
feature.

---

## MCP transport notes

The plugin ships a single stdio MCP server entry — `watermarkremover`
— configured in
[`servers.mcp.json`](../minimax-code/watermark-remover/servers.mcp.json).
StdIO is the right default for a local agent:

- **No auth required** — the stdio pipe is the auth boundary.
- **No port conflict** — nothing to bind.
- **Logs to stderr** — the `serve-mcp` CLI routes every log level to
  stderr, so the JSON-RPC stream on stdout stays clean. Per the
  [MCP stdio spec](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports),
  any text written to stdout that is not a `Content-Length`-framed
  JSON-RPC message breaks the stream and disconnects the agent.
- **30-second timeout** — the manifest sets `"timeout": 30000` (30 s)
  on the MCP server, matching the rest of the project's defaults.

If you need a remote / multi-user setup, swap the stdio transport for
the Streamable HTTP one. Edit
`servers.mcp.json` to:

```json
{
  "schemaVersion": 1,
  "mcpServers": {
    "watermarkremover": {
      "type": "streamable-http",
      "url": "http://localhost:5090/",
      "headers": {}
    }
  }
}
```

Then start the server on the same host:

```bash
watermarkremover serve-mcp --transport http --port 5090
```

The HTTP transport reuses the same `X-API-Key` middleware and
per-IP rate-limit as the regular `serve` command — see
[`docs/MCP.md → Streamable HTTP (stateless)`](./MCP.md#streamable-http-stateless)
for the configuration matrix.

---

## CLI fallback

When the MCP server is not registered (or the user wants to shell
out manually), the canonical command shape is:

```bash
# Text
echo "<text>" | watermarkremover clean-text --stdin
# or
watermarkremover clean-text "<text>"

# Markdown
watermarkremover clean-markdown < input.md

# Files (metadata stripping)
watermarkremover clean-file ./photo.jpg
watermarkremover clean-file ./docs/ --recursive

# Image (visual watermark removal; needs LaMa model)
watermarkremover clean-image photo.png -o clean.png

# Detection (no modification)
watermarkremover detect-text "pasted text"
watermarkremover detect-markdown < README.md
watermarkremover inspect-file photo.jpg
watermarkremover detect-watermark photo.png
```

The per-skill `run.sh` / `run.ps1` wrappers in
[`skills/`](../skills/) do exactly that for each format — they pipe
stdin to the right CLI subcommand and print the result.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Plugin toggled on but the agent reports no `clean_text` / `clean_file` tools | The MCP server failed at startup. Most often a missing `watermarkremover` binary on `$PATH`. | Install the binary (see [README → Installation](../README.md#-installation)) and re-enable the plugin. |
| `unknown command "serve-mcp"` from the binary | The binary on `$PATH` is an older build that doesn't ship the MCP command. | Upgrade to a build from the WR-S10 release or later (`--version` reports the build date). |
| `Unknown MCP transport 'sse'` in the server logs | Legacy SSE is not currently shipped. | Use `stdio` (the default) or `streamable-http`. See [`docs/MCP.md → Legacy SSE`](./MCP.md#legacy-sse-not-currently-shipped). |
| Agent disconnects immediately on first prompt | Something is writing to stdout. | Run the server standalone (`watermarkremover serve-mcp` in a terminal). Any non-empty stdout that is not a valid JSON-RPC frame breaks the stream. Logs are routed to stderr already; check your wrapper. |
| `File not found: …` on `clean_file` / `inspect_file` / `clean_image` | The agent's working directory is not what you think. | Pass an absolute `input_path` — the tools do not resolve relative paths against the agent's CWD. |
| `Unsupported file type: .ext` | The router only handles JPEG / PNG / WebP / PDF / DOCX / HTML. | For other formats, use a dedicated tool or wait for the metadata roadmap items (TIFF, HEIC, AVIF, …). |
| `modelUsed: "none"` in `clean_image` summary | The ONNX inpainting model is missing. | Run `watermarkremover download-model` to fetch the LaMa weights. The image is returned unchanged until then. |
| HTTP 401 on every request | `--api-key` is set on the server, the agent does not send `X-API-Key`. | Either unset `--api-key` (localhost dev) or configure the agent to send the header. |
| HTTP 429 after a few requests | Per-IP rate-limit is in effect. | Raise `mcp.rate_limit.permit_limit` in `config.yaml` or pass `--rate-limit N` on the CLI. |
| Slash commands don't show up in the TUI | The V1 manifest does not declare `commands`. | The MCP tools are still callable directly; see [Slash commands (forward-looking)](#slash-commands-forward-looking). |

---

## Assumptions and open questions

The plugin format used here is the **V1 local plugin spec** that the
MiniMax Code Desktop runtime accepts (`C:/Users/buzin/.minimax/plugins/<name>/`
in Windows, `~/.local/share/MiniMax/plugins/<name>/` on Linux). The
assumptions baked into the package:

- The plugin **directory and name match** (`watermark-remover`).
- The manifest sits under `.minimax-plugin/plugin.json` with
  `schemaVersion: 1`.
- The category is `Code` (matches the existing OpenCode / Claude Code
  pattern of treating this project as a developer tool).
- The icon is a PNG, ≤ 16 MiB, and lives at the package root.
- Every per-format skill's directory name and frontmatter `name`
  match — the V1 spec is strict about this, so the package uses the
  longer `watermark-clean-text` names (the shared `skills/` folder in
  the repo uses the shorter `clean-text` names; the *content* is the
  same).

If MiniMax Code ships a new plugin spec (V2, or a "slash commands are
first-class" change), the package is small and well-scoped enough to
migrate in a follow-up. The migration would mostly be:

- Rename `commands/` to whatever the new spec calls it.
- Add a `commands` (or equivalent) entry to the manifest.
- Possibly rename the per-format skill directories if the V2 spec
  loosens the directory / name match requirement.

Open questions that the spec doesn't address:

- Whether the V1 plugin runtime will accept the `commands/` folder
  silently (i.e. just ignore the unreferenced files) or fail the
  scan. The local spec says only manifest-declared files are surfaced
  — unreferenced files are not part of the package. We assume the
  former; if it turns out to be the latter, move the slash-command
  bodies into the master `SKILL.md` as a "Slash-style invocation"
  section.
- Whether MiniMax Code auto-discovers plugin icons, or requires the
  user to grant one explicitly. The V1 spec says the icon must
  reference an existing package PNG, which is what we ship.

Both of these would be cleared up in a 30-minute local test of the
package on a real MiniMax Code install; the package is ready for
that test.

---

## Reference

- 🤖 [`docs/MCP.md`](./MCP.md) — MCP server architecture, all 8 tool
  schemas, transport options.
- 🪝 [`docs/CLAUDE-CODE.md`](./CLAUDE-CODE.md) — the parallel
  Claude Code install recipe (project-local `mcp-config.json`,
  `~/.claude/settings.json` merge, `claude mcp add` one-liner).
- 🧠 [`docs/SKILLS.md`](./SKILLS.md) — skill resolution rules and
  per-skill deep-dive.
- 🏛️ [`docs/ARCHITECTURE.md`](./ARCHITECTURE.md) — module map and
  extension points.
- 🧭 [`BACKLOG.md` → WR-P623](../BACKLOG.md) — backlog entry; mirrors
  [`TODO.md` → WR-S17](../TODO.md).
- 🪝 [`minimax-code/watermark-remover/`](../minimax-code/watermark-remover/) —
  the plugin package itself.
