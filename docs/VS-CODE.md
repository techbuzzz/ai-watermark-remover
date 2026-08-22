# 🆚 Visual Studio Code integration

> **One-line summary.** Install the **WatermarkRemover** extension from
> the VS Code Marketplace, make sure the `watermarkremover` binary is on
> `$PATH`, and you can right-click any text selection → **Clean AI
> watermarks**, right-click any file in the Explorer → **Strip
> metadata**, and run **Detect AI watermarks** from the command palette
> — all without leaving the editor.

This page is the **end-to-end reference** for using WatermarkRemover
inside Visual Studio Code. It mirrors the
[`docs/CLAUDE-CODE.md`](./CLAUDE-CODE.md),
[`docs/MINIMAX-CODE.md`](./MINIMAX-CODE.md), and
[`docs/SKILLS.md`](./SKILLS.md) install recipes, with everything that's
specific to the VS Code extension and its three commands.

```text
extension:  vscode/watermark-remover/  (this repo, ships as a .vsix)
binary:     watermarkremover  (installed separately — Releases page or
                               `dotnet build` from source)
```

---

## Table of contents

- [Why VS Code](#why-vs-code)
- [What the extension ships](#what-the-extension-ships)
- [Install](#install)
  - [Marketplace (recommended)](#marketplace-recommended)
  - [From a `.vsix` file](#from-a-vsix-file)
  - [From source (development)](#from-source-development)
- [Usage](#usage)
  - [Clean a text selection](#clean-a-text-selection)
  - [Strip metadata from a file](#strip-metadata-from-a-file)
  - [Detect AI watermarks in a selection](#detect-ai-watermarks-in-a-selection)
  - [Command palette](#command-palette)
- [Extension settings](#extension-settings)
- [Source-mode development](#source-mode-development)
- [Bundled skills](#bundled-skills)
- [MCP integration](#mcp-integration)
- [Troubleshooting](#troubleshooting)
- [Reference](#reference)

---

## Why VS Code

VS Code is the most widely-used code editor, with a first-class
extension API and a built-in extension marketplace. Wiring
WatermarkRemover into VS Code gives every developer the same
**right-click → clean** experience they already have for refactor,
format, and organise-import, with zero new keyboard shortcut to learn.

The VS Code extension is intentionally **a thin client** over the
`watermarkremover` CLI. The extension does not re-implement any
cleaning logic — every command spawns the binary as a child process
and pipes data through it. This keeps the extension small (≈ 16 KB of
compiled JS) and means every fix in the CLI flows through to the
extension automatically.

The extension is also a **drop-in skills bundle**: the same
[`skills/`](../vscode/watermark-remover/skills/) folder the
[`WatermarkRemover.Mcp`](../src/WatermarkRemover.Mcp/) project ships
to Claude Code, OpenCode, MiniMax Code, Cursor, and Continue is
re-shipped inside the extension. AI assistants running inside VS Code
(Continue, Cline, …) pick up the `SKILL.md` files automatically and
learn when to call the WatermarkRemover MCP server or the extension
commands.

---

## What the extension ships

```
vscode/watermark-remover/
├── package.json                     # extension manifest
├── tsconfig.json                    # TypeScript build config
├── src/
│   └── extension.ts                 # entry point — registers the 3 commands
├── skills/                          # bundled AI-assistant skills
│   ├── watermark-remover/SKILL.md   # master skill (routing table)
│   ├── clean-text/SKILL.md
│   ├── clean-markdown/SKILL.md
│   ├── clean-file/SKILL.md
│   ├── clean-image/SKILL.md
│   └── detect/SKILL.md
├── test/
│   └── extension.test.js            # 16 structural tests (node --test)
├── README.md                        # marketplace listing
├── CHANGELOG.md
├── .vscodeignore                    # files excluded from the .vsix
└── .gitignore
```

Nothing is built at install time — the user installs the `.vsix`, VS
Code unpacks it, and the extension is ready.

---

## Install

### Marketplace (recommended)

1. Open VS Code.
2. Press `Ctrl+P` (Windows / Linux) or `Cmd+P` (macOS) and run
   **Extensions: Install Extensions** (or click the Extensions icon in
   the sidebar).
3. Search for **WatermarkRemover** (publisher `techbuzzz`).
4. Click **Install**.

The extension activates on first command invocation — there's no
startup hook, so the editor doesn't slow down.

### From a `.vsix` file

Download the latest `watermark-remover.vsix` from the project's
[GitHub Releases](https://github.com/techbuzzz/ai-watermark-remover/releases/latest):

```bash
# Linux / macOS
curl -L -O https://github.com/techbuzzz/ai-watermark-remover/releases/latest/download/watermark-remover.vsix
code --install-extension watermark-remover.vsix

# Windows PowerShell
Invoke-WebRequest -Uri https://github.com/techbuzzz/ai-watermark-remover/releases/latest/download/watermark-remover.vsix -OutFile watermark-remover.vsix
code --install-extension watermark-remover.vsix
```

### From source (development)

Useful when you're hacking on the extension itself:

```bash
git clone https://github.com/techbuzzz/ai-watermark-remover.git
cd ai-watermark-remover/vscode/watermark-remover
npm install
npm run build
# Then in VS Code: F5 → "Run Extension" (launches an Extension Development Host)
```

To package the built extension as a `.vsix` for local install:

```bash
npm install -g @vscode/vsce
vsce package
code --install-extension watermark-remover-1.0.0.vsix
```

---

## Usage

### Clean a text selection

1. Select any text in the editor (a few words, a paragraph, the whole
   file with `Ctrl+A` / `Cmd+A`).
2. Right-click → **WatermarkRemover: Clean AI watermarks from
   selection**.

The selection is replaced with the cleaned text. A status-bar
notification reports the number of characters removed (only shown if
something actually changed). The default behaviour strips
zero-width spaces, soft hyphens, bidi controls, Cyrillic homoglyphs
mixed with Latin, Claude / Gemini / OpenAI vendor signatures, and
frontmatter. To also enable Layer B statistical / synonym-aware
rewriting (EN + RU), turn on
[`watermarkremover.statistical`](#extension-settings).

### Strip metadata from a file

1. Right-click any file in the Explorer panel (or select multiple
   files with `Ctrl+Click` / `Cmd+Click`).
2. Click **WatermarkRemover: Strip metadata from selected file(s)**.

Each file gets a `<name>-clean<ext>` sibling with all EXIF / XMP /
IPTC / C2PA metadata stripped. The original is left untouched. The
extension reports how many files were cleaned, how many were skipped
(unsupported format or non-file URI), and which failed.

Supported formats (from the CLI's `IFileMetadataCleaner` registry):

| Format  | Extensions                  | Stripped                                                   |
|---------|-----------------------------|------------------------------------------------------------|
| JPEG    | `.jpg`, `.jpeg`             | EXIF, XMP, IPTC, ICC, C2PA, APP segments                   |
| PNG     | `.png`                      | `tEXt`, `zTXt`, `iTXt`, `eXIf` chunks                      |
| WebP    | `.webp`                     | EXIF, XMP, ICC RIFF chunks                                 |
| PDF     | `.pdf`                      | Document-info dictionary, XMP metadata                     |
| DOCX    | `.docx`                     | Core / extended / custom properties, revision history       |
| HTML    | `.html`, `.htm`             | `<meta name="generator|author">`, comments, trackers       |

Other formats exit with code 3 ("unsupported") and the extension
silently skips them.

### Detect AI watermarks in a selection

1. Select any text in the editor.
2. Right-click → **WatermarkRemover: Detect AI watermarks in
   selection**.

A new editor tab opens with the JSON result, formatted and syntax-
highlighted:

```json
[
  {
    "vendor": "claude",
    "kind": "zwsp",
    "evidence": "U+200B at offset 14"
  },
  {
    "vendor": "openai",
    "kind": "homoglyph",
    "evidence": "Latin 'a' (U+0430) inside Latin word"
  }
]
```

The selection is **not modified** — `detect-text` is a read-only
operation. This is the right command when the user wants a forensic
check without changing the buffer.

### Command palette

`Ctrl+Shift+P` / `Cmd+Shift+P` → search for any of:

- `WatermarkRemover: Clean AI watermarks from selection`
- `WatermarkRemover: Detect AI watermarks in selection`
- `WatermarkRemover: Strip metadata from selected file(s)`

The commands are scoped via `when` clauses so they only appear when
their precondition holds (text selected, or a file is right-clicked).

---

## Extension settings

Open **File → Preferences → Settings** and search for "WatermarkRemover":

| Setting                              | Default              | Description                                                                 |
|--------------------------------------|----------------------|-----------------------------------------------------------------------------|
| `watermarkremover.binaryPath`        | `watermarkremover`   | Path to the CLI binary. Set to an absolute path if it's not on `$PATH`.    |
| `watermarkremover.preferMcp`         | `false`              | Reserved for future MCP-direct routing. CLI is used today.                  |
| `watermarkremover.statistical`       | `false`              | When `true`, the `cleanText` command also runs Layer B synonym rewriting.  |
| `watermarkremover.showNotifications` | `true`               | Show status-bar / information messages after each operation.                |

The settings are exposed via VS Code's standard settings UI — no
custom HTML, no extra schema.

---

## Source-mode development

If you're hacking on the CLI itself, point the extension at a
`dotnet run` wrapper so every extension command triggers a fresh
build.

1. Save this as `~/bin/wr-dev` (POSIX) or `C:\bin\wr-dev.cmd`
   (Windows) and make it executable:

   ```bash
   #!/usr/bin/env bash
   # ~/bin/wr-dev
   exec dotnet run --project /path/to/ai-watermark-remover/src/WatermarkRemover.CLI -- "$@"
   ```

   ```cmd
   @echo off
   :: C:\bin\wr-dev.cmd
   dotnet run --project C:\path\to\ai-watermark-remover\src\WatermarkRemover.CLI -- %*
   ```

2. In VS Code, open **File → Preferences → Settings**, search for
   `watermarkremover.binaryPath`, and set it to the wrapper's full
   path (e.g. `/Users/you/bin/wr-dev` or
   `C:\bin\wr-dev.cmd`).

3. Save. The next extension command spawns `dotnet run` for the
   real CLI — slower per call, but you never need to manually
   rebuild the binary.

The wrapper is invoked with the same args as the production binary
(`clean-text --stdin`, `clean-file <path>`, `detect-text --stdin
--json`), so no extension code change is required.

---

## Bundled skills

The extension's
[`skills/`](../vscode/watermark-remover/skills/) directory ships the
same five per-format skills the CLI exposes to other agents, plus a
master `watermark-remover` skill that VS Code-friendly agents
(Continue, Cline, the GitHub Copilot Chat agent in agent mode, …)
pick up automatically. The frontmatter `compatibility` field lists
`vscode, opencode, claude-code, minimax-code, cursor, continue` so
any agent that consumes the SKILL.md format will learn the tool.

The skills are also useful when the user has Continue or Cline
configured inside VS Code — those agents will read `SKILL.md` and
call the WatermarkRemover MCP server (or the extension commands)
without any extra wiring.

See [`docs/SKILLS.md`](./SKILLS.md) for the full reference.

---

## MCP integration

The VS Code extension and the MCP server are **complementary**, not
mutually exclusive. The extension is the UI layer for human-driven
operations (right-click a selection, right-click a file). The MCP
server is the integration layer for AI-driven operations (the agent
calls `clean_text` itself).

To use both side by side:

1. Install the extension (this package).
2. Register the MCP server in VS Code's MCP hosts (or in a
   per-workspace `.vscode/mcp.json`):

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

3. Reload VS Code. The MCP server registers 8 tools, the extension
   registers 3 commands. AI assistants get the tools; humans get the
   right-click menus.

See [`docs/MCP.md`](./MCP.md) for the full MCP server reference.

---

## Troubleshooting

| Symptom                                                                 | Cause                                                              | Fix                                                                                       |
|-------------------------------------------------------------------------|--------------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| Right-click shows the commands but clicking does nothing                | `watermarkremover` binary not on `$PATH`                            | Install from [Releases](https://github.com/techbuzzz/ai-watermark-remover/releases/latest), or set `watermarkremover.binaryPath` to an explicit path |
| "Failed to spawn 'watermarkremover': spawn ENOENT"                      | Binary missing                                                     | Same as above; the extension also shows a click-through install instructions button        |
| `clean-file` on a `.txt` does nothing                                    | The CLI only supports the formats in the table above; `.txt` is text | Use `clean-text` instead, or read it as markdown                                           |
| `clean-file` on a `.psd` / `.ai` / `.sketch` reports "unsupported"      | Format not in the cleaner registry                                 | Open a feature request — see [BACKLOG.md → P1](../BACKLOG.md#p1--core-features-v10)         |
| Right-click menus don't appear on a remote / WSL workspace              | VS Code's `editor/context/contextual` is a relatively new menu slot | Fall back to the explicit `command palette` invocation; the menus will appear on next reload |
| `clean-image` is not a command in the extension                          | Inpainting needs the LaMa ONNX model, which the extension does not auto-download | Run `watermarkremover download-model` from the terminal, then `watermarkremover clean-image` directly — the extension is text-and-metadata-only by design |
| The bundled skills are not picked up by Continue / Cline                 | Those agents need an explicit `.continue/`, `.cursor/`, or `.cline/` skills directory | Copy the `skills/` folder into the agent's skills dir, or use the `skills/install.sh --agent <name>` script from the repo root |

---

## Reference

- Project README: [`README.md`](../README.md)
- MCP server reference: [`docs/MCP.md`](./MCP.md)
- Skills reference: [`docs/SKILLS.md`](./SKILLS.md)
- Claude Code install (sister integration): [`docs/CLAUDE-CODE.md`](./CLAUDE-CODE.md)
- MiniMax Code install (sister integration): [`docs/MINIMAX-CODE.md`](./MINIMAX-CODE.md)
- Architecture overview: [`docs/ARCHITECTURE.md`](./ARCHITECTURE.md)
- VS Code extension source: [`vscode/watermark-remover/`](../vscode/watermark-remover/)
- Issues & feature requests: [github.com/techbuzzz/ai-watermark-remover/issues](https://github.com/techbuzzz/ai-watermark-remover/issues)
