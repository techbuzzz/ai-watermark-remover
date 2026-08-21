# 🤖 Claude Code integration

> **One-line summary.** Two commands and Claude Code can call
> `clean_text`, `clean_file`, `clean_image`, `detect_text`, and the
> other five MCP tools — and an optional hook auto-cleans text the
> user pastes into the prompt.

This page is the user-and-developer reference for wiring
WatermarkRemover into [Claude Code](https://claude.com/claude-code)
via the [Model Context Protocol](https://modelcontextprotocol.io/)
server. The two paragraphs that follow are the *whole* integration in
ninety seconds; the [install](#install) recipes and
[reference](#reference) below cover every shape (project-local,
`~/.claude/`-global, MCP-only, hook-only, both).

```bash
# 1. Make sure the `watermarkremover` binary is on $PATH
#    (see README → Installation).

# 2. Register the MCP server with Claude Code (one command, persistent).
claude mcp add watermarkremover -- watermarkremover serve-mcp
```

Restart Claude Code. `clean_text`, `clean_file`, and the other six
tools appear in the tool picker and the agent learns when to use them
from the [`.claude/skills/watermark-remover/SKILL.md`](../.claude/skills/watermark-remover/SKILL.md)
shipped at the project root. Optionally add a
[`UserPromptSubmit` hook](#auto-clean-pasted-text-optional) to
auto-clean text the user pastes into the prompt.

---

## Table of contents

- [Why Claude Code](#why-claude-code)
- [What this integration ships](#what-this-integration-ships)
- [Install](#install)
  - [One-liner (recommended)](#one-liner-recommended)
  - [Project-local `mcp-config.json`](#project-local-mcp-configjson)
  - [Global `~/.claude/settings.json` merge](#global-claudesettingsjson-merge)
  - [From source](#from-source)
- [Verify the install](#verify-the-install)
- [Auto-clean pasted text (optional)](#auto-clean-pasted-text-optional)
  - [What the hook does](#what-the-hook-does)
  - [Project-local install](#project-local-hook-install)
  - [Global install (via `skills/install.sh`)](#global-hook-install-via-skillsinstallsh--agent-claude)
  - [Tuning the hook](#tuning-the-hook)
- [What the agent sees](#what-the-agent-sees)
- [Troubleshooting](#troubleshooting)
- [Reference](#reference)

---

## Why Claude Code

Claude Code is the official Anthropic CLI for Claude that runs locally
on the developer's machine and reads / writes the filesystem, calls
Bash, and drives git. It speaks MCP natively, so registering the
`watermarkremover` server gives the agent the same eight tools that
OpenCode, MiniMax Code, Cursor, and Continue get — see
[`docs/MCP.md`](./MCP.md) for the tool reference and
[`docs/ARCHITECTURE.md`](./ARCHITECTURE.md) for the data flow.

The Claude Code-specific extras this project ships:

- **A project-local skill** at
  [`.claude/skills/watermark-remover/SKILL.md`](../.claude/skills/watermark-remover/SKILL.md)
  so a fresh `git clone` plus a `claude mcp add` is enough — no extra
  install step required for the agent to learn the tool.
- **A drop-in `mcp-config.json`** for users who prefer a
  project-checked-in config over the global `~/.claude/` registry.
- **An optional `UserPromptSubmit` hook** that pipes pasted text
  through `clean_text` and injects the cleaned version back as
  context — invisible in the common case, only fires when the input
  actually contained invisible / zero-width / homoglyph characters.

---

## What this integration ships

```
.claude/
└── skills/
    └── watermark-remover/
        ├── SKILL.md            # master skill — taught to Claude Code
        ├── mcp-config.json     # drop-in mcpServers snippet
        ├── hooks.json          # drop-in UserPromptSubmit hook snippet
        └── hooks/
            └── auto-clean.js   # node script behind the hook
docs/
└── CLAUDE-CODE.md              # this file
```

All four files are checked in at the project root. Nothing is built;
everything is consumed at agent-startup time.

---

## Install

Pick the shape that matches how you manage Claude Code configuration.
The **one-liner** is the right call for most people; the other two
are for projects where the MCP registration is checked into the
repo (so contributors get it for free) and for users who want a
fully global install.

### One-liner (recommended)

The fastest install — registers the `watermarkremover` server in
Claude Code's global store, survives across sessions, and applies to
every project you open in that Claude Code install:

```bash
# Release binary on $PATH
claude mcp add watermarkremover -- watermarkremover serve-mcp

# Or, from source (one-off, while iterating)
claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp
```

> The `claude mcp add` form was introduced in Claude Code 1.0 and
> persists the registration in `~/.claude.json` (or
> `~/.config/claude/settings.json` on Linux). It is the supported
> shape as of this writing. Run `claude mcp add --help` to confirm
> the exact flag for your build.

### Project-local `mcp-config.json`

For projects that want the MCP registration checked into the repo so
contributors don't need to remember the `claude mcp add` step, copy
the contents of
[`.claude/skills/watermark-remover/mcp-config.json`](../.claude/skills/watermark-remover/mcp-config.json)
into the **project root** as `.mcp.json`:

```bash
# From the repo root
cp .claude/skills/watermark-remover/mcp-config.json .mcp.json
```

Claude Code auto-discovers `.mcp.json` from the project root at
startup, so no further wiring is needed. The file looks like:

```json
{
  "mcpServers": {
    "watermarkremover": {
      "type": "stdio",
      "command": "watermarkremover",
      "args": ["serve-mcp"]
    }
  }
}
```

> **Why this shape?** The `mcpServers` key is the project-level
> counterpart of `claude mcp add` — same fields, same stdio
> transport, but lives in the repo so it's reviewed alongside
> the rest of the project config.

### Global `~/.claude/settings.json` merge

For a fully global install that survives across projects, merge the
`mcpServers` block into `~/.claude/settings.json`:

```jsonc
{
  // ... your existing settings ...
  "mcpServers": {
    "watermarkremover": {
      "type": "stdio",
      "command": "watermarkremover",
      "args": ["serve-mcp"]
    }
  }
}
```

If the file already has an `mcpServers` key, **merge** the
`watermarkremover` entry into the existing object rather than
replacing it.

### From source

If you don't have a release binary and want Claude Code to build on
demand, swap the `command` + `args` for `dotnet run`:

```bash
claude mcp add watermarkremover -- dotnet run --project /absolute/path/to/ai-watermark-remover/src/WatermarkRemover.CLI -- serve-mcp
```

The first call is slow (build) but subsequent ones are fast.
Equivalent swap in `mcp-config.json` / `~/.claude/settings.json`:

```json
{
  "mcpServers": {
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

---

## Verify the install

After registering and restarting Claude Code, run any of:

```bash
claude mcp list           # 'watermarkremover' row should show 'connected'
claude mcp get watermarkremover
```

The `mcp get` form prints the resolved command, args, and transport
plus the server's reported tool list. The eight tools prefixed
`clean_…` / `detect_…` / `inspect_…` should all be present.

A live smoke test (no agent required) is to start the server
standalone and confirm it waits silently on stdin:

```bash
watermarkremover serve-mcp
# → stderr: "WatermarkRemover MCP server starting (stdio transport)."
# → stdout: nothing (the JSON-RPC channel — silence is correct).
```

Send a `tools/list` JSON-RPC frame on stdin to see the same eight
tools the agent sees. The full tool reference is in
[`docs/MCP.md → Tool reference`](./MCP.md#tool-reference).

---

## Auto-clean pasted text (optional)

Claude Code supports lifecycle [hooks](https://code.claude.com/docs/en/hooks)
that fire on events like `UserPromptSubmit`, `PreToolUse`, `Stop`.
The shipped `hooks.json` registers a `UserPromptSubmit` command hook
that pipes the user's pasted text through `clean_text` and injects
the cleaned version back as `hookSpecificOutput.additionalContext`.
The agent then sees both the original and the cleaned text side by
side, and can choose to work from either.

### What the hook does

```
User submits prompt
       │
       ▼
┌──────────────────────┐    JSON on stdin
│ UserPromptSubmit     │ ────────────────┐
│ hook                 │                 ▼
│ (auto-clean.js)      │ ◀──────────  extract .prompt
└──────────┬───────────┘
           │  spawnSync("watermarkremover", ["clean-text", "--stdin"])
           ▼
   clean_text cleans the text  (ZWSP / soft hyphens / homoglyphs)
           │
           │  if cleaned ≠ original
           ▼
   write JSON { hookSpecificOutput: { additionalContext: "..." } } on stdout
           │
           ▼
   Claude Code injects additionalContext into the conversation
```

**Failure modes (all silent):**

- The `watermarkremover` binary is not on `$PATH` — the hook emits
  no context; the prompt is processed unchanged.
- The CLI returns non-zero — the hook emits no context.
- The cleaned text equals the input — the hook emits no context.

The hook only ever **adds** context; it never blocks the prompt and
never modifies what the user typed.

### Project-local hook install

1. Open the project's `.claude/settings.json` (create it if it
   doesn't exist).
2. Merge the `hooks` key from
   [`.claude/skills/watermark-remover/hooks.json`](../.claude/skills/watermark-remover/hooks.json)
   into the existing settings:

   ```jsonc
   {
     // ... your existing settings ...
     "hooks": {
       "UserPromptSubmit": [
         {
           "hooks": [
             {
               "type": "command",
               "timeout": 25,
               "command": "node .claude/skills/watermark-remover/hooks/auto-clean.js"
             }
           ]
         }
       ]
     }
   }
   ```

3. Restart Claude Code. Paste a block of text that contains a
   `U+200B` (zero-width space) into the prompt. The agent should
   report that it sees both the original and a `[watermarkremover
   auto-clean]`-prefixed cleaned version.

### Global hook install (via `skills/install.sh --agent claude`)

For a global install — applies to every project — copy the skill
folder into `~/.claude/skills/watermarkremover/` and adjust the
script path:

```bash
# From the repo root
./skills/install.sh --agent claude
# → /home/<you>/.claude/skills/watermarkremover/watermark-remover/
```

Then merge into `~/.claude/settings.json`:

```jsonc
{
  "hooks": {
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "timeout": 25,
            "command": "node ~/.claude/skills/watermarkremover/watermark-remover/hooks/auto-clean.js"
          }
        ]
      }
    ]
  }
}
```

The path differs because the skills installer prefixes every skill
folder with a `watermarkremover/` namespace directory — see
[`docs/SKILLS.md`](./SKILLS.md) for the resolution rules.

### Tuning the hook

The shipped script is intentionally minimal. Common tweaks:

- **Add `statistical: true`** for EN / RU synonym rewriting — open
  `auto-clean.js` and change the `args` array from
  `['clean-text', '--stdin']` to `['clean-text', '--stdin',
  '--statistical']`. Off by default because Layer B rephrases
  prose and the user may not have asked for that.
- **Only clean prompts longer than N characters** — add a guard
  after the `prompt` extraction: `if (prompt.length < 50) return;`
- **Log to a file instead of stdout** — replace the
  `process.stdout.write` call with a `fs.appendFileSync('/tmp/
  watermarkremover-hook.log', response)`. Useful while debugging
  why a particular prompt is / is not being cleaned.
- **Switch to a per-project Python or sh wrapper** — Claude Code
  supports any executable as a `command` hook. Replace the
  `command` string with a path to a shell script that does the
  same parse → spawn → JSON-emit dance.

---

## What the agent sees

After install, Claude Code reads
[`.claude/skills/watermark-remover/SKILL.md`](../.claude/skills/watermark-remover/SKILL.md)
at startup and the eight MCP tools become available. The skill
teaches the agent:

- **When to use the tool** — the trigger conditions (pasted text
  with invisible characters, JPEG with EXIF, "is this AI-generated?").
- **Routing table** — which tool to call for which user intent.
- **CLI fallback** — the canonical `watermarkremover clean-text
  <text>` shape when MCP is unavailable.
- **Error contract** — what to tell the user on `IsError: true`,
  unsupported file extensions, missing LaMa model.

The five per-format skills (`watermark-clean-text`,
`watermark-clean-markdown`, `watermark-clean-file`,
`watermark-clean-image`, `watermark-detect`) live under
[`skills/`](../skills/) and have the deep per-format reference. They
are auto-discovered by Claude Code if copied into
`~/.claude/skills/watermarkremover/` via
`./skills/install.sh --agent claude` — the installer copies each
`skills/<name>/` folder verbatim into
`~/.claude/skills/watermarkremover/<name>/`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `claude mcp list` shows nothing for `watermarkremover` | The `claude mcp add` command was not run, or the binary is not on `$PATH`. | Re-run the one-liner; confirm `which watermarkremover` returns a path. |
| `claude mcp get watermarkremover` shows `disconnected` or `error` | The `watermarkremover` binary crashed at startup, or its logs are filling stdout. | Run `watermarkremover serve-mcp` standalone. Any non-empty stdout that is not a valid JSON-RPC frame breaks the stream — see [`docs/MCP.md → stdio`](./MCP.md#stdio-default). |
| The hook fires but nothing changes in the conversation | The pasted text had no invisible characters (or the cleaned text equals the input). | Try pasting `Hello\u200BWorld`. The hook only adds context when the cleaned output differs. |
| `node: command not found` from the hook | `node` is not on `$PATH`. | Install Node.js ≥ 18, or replace the hook with a Python equivalent (see [Tuning the hook](#tuning-the-hook)). |
| Hook times out (>25 s) | The CLI is reading from stdin without `--stdin`. | Open `auto-clean.js` and confirm `args: ['clean-text', '--stdin']` is intact. |
| `modelUsed: "none"` in `clean_image` responses | The LaMa ONNX model is missing. | Run `watermarkremover download-model` — the image is returned unchanged until the model is downloaded. |
| Agent reports `Unknown tool: clean_text` | The MCP server is registered but the tool name does not match. | Run `claude mcp get watermarkremover` and confirm `tools/list` includes `clean_text`. The full tool reference is in [`docs/MCP.md`](./MCP.md#tool-reference). |

---

## Reference

- 📘 [docs/MCP.md](./MCP.md) — `serve-mcp` architecture, transports, all 8
  tool schemas, install recipes for every MCP host.
- 🧠 [docs/SKILLS.md](./SKILLS.md) — the five per-format skills under
  `skills/`, installer, resolution rules.
- 🏛️ [docs/ARCHITECTURE.md](./ARCHITECTURE.md) — module map, data flow,
  extension points.
- 🪝 [Claude Code hooks reference](https://code.claude.com/docs/en/hooks)
  — full hook schema, matcher rules, exit codes, `additionalContext`
  injection contract.
- 🤖 [Model Context Protocol spec](https://modelcontextprotocol.io/)
  — stdio / Streamable HTTP transport, tool / resource / prompt
  primitives.
