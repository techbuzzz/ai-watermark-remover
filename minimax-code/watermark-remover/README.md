# WatermarkRemover — MiniMax Code Plugin

A first-class MiniMax Code plugin that exposes the WatermarkRemover
pipeline as MCP tools and skills. Drop this folder into MiniMax Code's
plugin directory and the agent will learn how to strip AI watermarks
from text, markdown, documents, and images.

## Layout

```
minimax-code/watermark-remover/
├── .minimax-plugin/
│   └── plugin.json              # V1 manifest (required)
├── icon.png                     # Plugin icon (128 KB, from Code category pool)
├── servers.mcp.json             # stdio MCP server config
├── skills/
│   ├── watermark-remover/SKILL.md       # master skill (routing table)
│   ├── watermark-clean-text/SKILL.md    # text cleaning
│   ├── watermark-clean-markdown/SKILL.md
│   ├── watermark-clean-file/SKILL.md
│   ├── watermark-clean-image/SKILL.md
│   └── watermark-detect/SKILL.md
├── commands/                    # forward-looking slash commands
│   ├── wr-clean-text.md
│   ├── wr-clean-file.md
│   └── wr-detect.md
└── README.md                    # this file
```

## Install

Copy the whole `watermark-remover/` directory into MiniMax Code's
plugin folder:

| OS      | Path                                                    |
|---------|---------------------------------------------------------|
| Linux   | `~/.local/share/MiniMax/plugins/watermark-remover/`     |
| macOS   | `~/Library/Application Support/MiniMax/plugins/watermark-remover/` |
| Windows | `%APPDATA%\MiniMax\plugins\watermark-remover\`          |

Then enable the `WatermarkRemover` plugin in the MiniMax Code Plugins
pane. The MCP server (`watermarkremover serve-mcp`) will start on
the next agent launch.

## Prerequisites

The `watermarkremover` binary must be on `$PATH`. The plugin's
`servers.mcp.json` invokes it as:

```
watermarkremover serve-mcp
```

Install options:

- **Release binary** — grab the platform archive from
  [GitHub Releases](https://github.com/techbuzzz/ai-watermark-remover/releases),
  extract, and put the binary on `$PATH`.
- **From source** — `dotnet tool install --local` after `dotnet
  publish -c Release -r <rid>`, or run `dotnet run --project
  src/WatermarkRemover.CLI -- serve-mcp` for development.
- **Docker** — `techbuzzz/watermarkremover:latest` ships the same
  `serve-mcp` command; bind to a port and use the HTTP transport.

See [`README → Installation`](../../README.md#-installation) for the
full matrix.

## What the agent sees

When the plugin is enabled, the agent gets:

- **One MCP server** — `watermarkremover` — exposing 8 tools:
  `clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
  `detect_text`, `detect_markdown`, `inspect_file`, `detect_watermark`.
- **Six skills** — the master `watermark-remover` skill plus the five
  per-format skills. The agent reads the YAML frontmatter `description`
  field to decide when to activate each one.
- **Three slash commands** (forward-looking) — `/wr-clean-text`,
  `/wr-clean-file`, `/wr-detect`. These are shipped as files; whether
  they surface in the MiniMax Code TUI depends on whether the agent
  version auto-discovers them. The agent can still be invoked by the
  user typing the underlying MCP tool name directly.

## Reference

- 📘 [`docs/MINIMAX-CODE.md`](../../docs/MINIMAX-CODE.md) — full
  install + troubleshooting reference.
- 🤖 [`docs/MCP.md`](../../docs/MCP.md) — MCP server architecture,
  transport options, all 8 tool schemas.
- 🧠 [`docs/SKILLS.md`](../../docs/SKILLS.md) — skill resolution and
  per-skill deep-dive.
- 🏛️ [`docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) — module
  map and extension points.
