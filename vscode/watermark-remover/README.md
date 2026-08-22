# WatermarkRemover — Visual Studio Code extension

> **One-line summary.** Strip AI-provenance watermarks from text (invisible
> characters, vendor signatures) and from files (EXIF / XMP / C2PA / IPTC
> metadata) right inside VS Code. Right-click selected text → **Clean AI
> watermarks**, right-click a file in the Explorer → **Strip metadata**,
> command palette → **Detect AI watermarks**.

The extension is a **thin UI layer** over the [WatermarkRemover
CLI](https://github.com/techbuzzz/ai-watermark-remover) — it does not
re-implement any cleaning logic. Every command spawns the
`watermarkremover` binary as a child process and pipes data through it.

## Features

- ✂️ **Clean text** — select text, right-click → **WatermarkRemover: Clean
  AI watermarks from selection**. Strips zero-width spaces, soft hyphens,
  bidi controls, Cyrillic homoglyphs mixed with Latin, Claude / Gemini /
  OpenAI vendor signatures, and frontmatter. Optionally enables Layer B
  statistical / synonym-aware rewriting (EN + RU) when you turn on
  `watermarkremover.statistical`.
- 🗂️ **Strip metadata** — right-click any file in the Explorer →
  **WatermarkRemover: Strip metadata from selected file(s)**. Removes
  EXIF, XMP, IPTC, C2PA, and ICC chunks from JPEG / PNG / WebP / TIFF
  images, OpenXML core / extended / custom properties from DOCX / PPTX /
  XLSX, and the document-info / XMP dictionary from PDFs. Writes a
  `<name>-clean<ext>` copy alongside the original.
- 🔍 **Detect AI watermarks** — select text → **WatermarkRemover: Detect
  AI watermarks in selection**. Opens the result in a new tab as a
  formatted JSON document (vendor, kind, evidence).
- ⚙️ **Configurable** — point at a custom binary, toggle
  per-command flags, or silence notifications entirely.

## Requirements

The extension itself ships as a single ~16 KB `.vsix` and runs in
VS Code ≥ **1.85**. It needs the `watermarkremover` binary on
`$PATH` (or the explicit path you set in
`watermarkremover.binaryPath`).

| Platform    | Install                                                                  |
|-------------|--------------------------------------------------------------------------|
| Linux / macOS | `curl -L https://github.com/techbuzzz/ai-watermark-remover/releases/latest/download/watermarkremover-linux-x64.zip \| funzip \| sudo tee /usr/local/bin/watermarkremover > /dev/null && sudo chmod +x /usr/local/bin/watermarkremover` |
| Windows     | Download `watermarkremover-win-x64.zip` from [Releases](https://github.com/techbuzzz/ai-watermark-remover/releases/latest), expand, and add the folder to `PATH` |
| From source | `git clone https://github.com/techbuzzz/ai-watermark-remover && cd ai-watermark-remover && dotnet build` (then set `watermarkremover.binaryPath` to `dotnet` with args — see below) |

You can verify the install from a terminal:

```bash
watermarkremover --version
# → watermarkremover 1.0.0
```

## Install the extension

### From the VS Code Marketplace

1. Open VS Code
2. Press `Ctrl+P` / `Cmd+P` and run **Extensions: Install Extensions**
3. Search for **WatermarkRemover** (publisher `techbuzzz`)
4. Click **Install**

### From a `.vsix` file

```bash
# Download the latest release from GitHub
curl -L -O https://github.com/techbuzzz/ai-watermark-remover/releases/latest/download/watermark-remover.vsix
# Install it
code --install-extension watermark-remover.vsix
```

### From source (development)

```bash
git clone https://github.com/techbuzzz/ai-watermark-remover.git
cd ai-watermark-remover/vscode/watermark-remover
npm install
npm run build
# Then in VS Code: F5 → "Run Extension" (launches an Extension Development Host)
```

## Usage

### Right-click a text selection

1. Select any text in the editor (a few words, a paragraph, a whole file
   with `Ctrl+A`).
2. Right-click → **WatermarkRemover: Clean AI watermarks from selection**.
3. The selection is replaced with the cleaned text. A status-bar
   notification reports the number of characters removed (only shown if
   something actually changed).

To **detect** watermarks without modifying the buffer, use
**WatermarkRemover: Detect AI watermarks in selection** — opens a
formatted JSON document in a new tab.

### Right-click a file in the Explorer

1. Right-click any file in the Explorer panel (or select multiple files).
2. Click **WatermarkRemover: Strip metadata from selected file(s)**.
3. Each file gets a `<name>-clean<ext>` sibling with all EXIF / XMP /
   IPTC / C2PA metadata stripped. The original is left untouched.

### Command palette

Press `Ctrl+Shift+P` / `Cmd+Shift+P` and search for any of:

- `WatermarkRemover: Clean AI watermarks from selection`
- `WatermarkRemover: Detect AI watermarks in selection`
- `WatermarkRemover: Strip metadata from selected file(s)`

## Extension settings

This extension contributes the following settings (under
**File → Preferences → Settings → WatermarkRemover**):

| Setting                          | Default              | Description                                                                 |
|----------------------------------|----------------------|-----------------------------------------------------------------------------|
| `watermarkremover.binaryPath`    | `watermarkremover`   | Path to the CLI binary. Set to an absolute path if it's not on `$PATH`.    |
| `watermarkremover.preferMcp`     | `false`              | Reserved for future MCP-direct routing. CLI is used today.                  |
| `watermarkremover.statistical`   | `false`              | When `true`, the `cleanText` command also runs Layer B synonym rewriting.  |
| `watermarkremover.showNotifications` | `true`           | Show status-bar / information messages after each operation.                |

### Source-mode development

If you're hacking on the CLI itself, set
`watermarkremover.binaryPath` to a wrapper script that shells out to
`dotnet run`. For example, save this as `~/bin/wr-dev` and `chmod +x` it:

```bash
#!/usr/bin/env bash
exec dotnet run --project /path/to/ai-watermark-remover/src/WatermarkRemover.CLI -- "$@"
```

Then set `watermarkremover.binaryPath` to `/Users/you/bin/wr-dev`. The
extension will spawn `dotnet run` for every command — slower per call,
but you can iterate without rebuilding the binary.

## Bundled skills

This extension ships the same `skills/` folder the CLI exposes to other
AI assistants. The bundled `SKILL.md` files teach the agent **when** to
call WatermarkRemover and **how** to wire the MCP server, the CLI, and
this extension. Drop-in install for any other agent that consumes the
`SKILL.md` format.

See [`skills/`](./skills/) for the master skill and the five per-format
skills (`watermark-clean-text`, `watermark-clean-markdown`,
`watermark-clean-file`, `watermark-clean-image`, `watermark-detect`).

## Commands

| Command                                | Title                                                       | When                              |
|----------------------------------------|-------------------------------------------------------------|-----------------------------------|
| `watermarkremover.cleanText`           | WatermarkRemover: Clean AI watermarks from selection        | Editor has a non-empty selection  |
| `watermarkremover.detectText`          | WatermarkRemover: Detect AI watermarks in selection         | Editor has a non-empty selection  |
| `watermarkremover.cleanFile`           | WatermarkRemover: Strip metadata from selected file(s)      | A file is selected in the Explorer|

## Context-menu integration

| Menu                          | Command                  |
|-------------------------------|--------------------------|
| `editor/context`              | `cleanText`, `detectText`|
| `editor/context/contextual`   | `cleanText`, `detectText`|
| `explorer/context`            | `cleanFile`              |
| `explorer/context/contextual` | `cleanFile`              |

## Architecture

The extension is intentionally **dependency-free at runtime** — it only
uses the Node built-ins (`child_process`, `node:fs`) and the `vscode`
module. There is no bundler, no React, no webpack. The bundled output
is a single `out/extension.js` file (≈16 KB minified).

```
                       ┌──────────────────────────┐
                       │  VS Code editor          │
                       │  (selection / file URI)  │
                       └────────────┬─────────────┘
                                    │  user gesture
                                    ▼
                       ┌──────────────────────────┐
                       │  vscode-watermark-remover │
                       │  (this extension)        │
                       └────────────┬─────────────┘
                                    │  spawn + stdio
                                    ▼
                       ┌──────────────────────────┐
                       │  watermarkremover CLI    │
                       │  (clean-text / clean-file│
                       │   / detect-text)         │
                       └──────────────────────────┘
```

If you also register the [WatermarkRemover MCP
server](https://github.com/techbuzzz/ai-watermark-remover/blob/main/docs/MCP.md)
in VS Code's MCP hosts, AI assistants (Claude Code, Continue, …) will
get the full 8-tool set in addition to the 3 commands this extension
exposes.

## Known limitations

- **The binary must be installed separately** — the extension does not
  bundle or download it. The first time you run a command without the
  binary on `$PATH`, you'll get a clear install-instructions message.
- **`preferMcp` is reserved** — VS Code's MCP API is still new. The
  CLI path is the supported route today; flip `preferMcp` to `true`
  once the MCP-direct path is implemented.
- **Image inpainting is not a command here** — visual watermark removal
  (the `clean-image` CLI subcommand) needs the LaMa ONNX model
  downloaded. Run it from the terminal instead; see
  [`docs/MCP.md`](https://github.com/techbuzzz/ai-watermark-remover/blob/main/docs/MCP.md).

## Contributing

Issues and PRs welcome at
[github.com/techbuzzz/ai-watermark-remover](https://github.com/techbuzzz/ai-watermark-remover).
The extension source lives under
[`vscode/watermark-remover/`](https://github.com/techbuzzz/ai-watermark-remover/tree/main/vscode/watermark-remover)
in the main repo.

## License

MIT — same as the parent project. See
[`LICENSE`](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE).
