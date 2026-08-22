---
name: watermark-remover
description: >
  Use when the user pastes AI-generated text, asks to strip invisible
  characters / ZWSP / Cyrillic homoglyphs, wants to clean metadata from
  a JPEG / PNG / PDF / DOCX / HTML / WebP file before sharing, or wants to
  know whether a text blob carries a Claude / Gemini / OpenAI / DeepSeek /
  Grok watermark. Routes to the `watermarkremover` CLI, the registered MCP
  server (when the host supports it), or the VS Code extension's three
  commands (`watermarkremover.cleanText`, `watermarkremover.cleanFile`,
  `watermarkremover.detectText`). The project also ships the same five
  per-format skills (`clean-text`, `clean-markdown`, `clean-file`,
  `clean-image`, `detect`) under `skills/`.
license: MIT
compatibility: vscode, opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-remover`

Master skill for the WatermarkRemover integration. This is the entry
point: it teaches the agent **when** to think about WatermarkRemover and
points at the right tool / command / CLI invocation. The five
per-format skills (clean-text, clean-markdown, clean-file, clean-image,
detect) live in [`skills/`](../) and have the deep per-format reference.

## When to use

Activate this skill when the user:

- Pastes a block of text that "looks weird", has stray invisible
  characters, mixes Latin and Cyrillic look-alikes, or feels
  AI-generated.
- Asks to "clean", "normalise", "strip watermarks", "remove AI traces",
  "humanise", or "rewrite" content.
- Mentions ZWSP, zero-width characters, homoglyphs, U+200B, soft hyphens.
- Uploads a JPEG / PNG / PDF / DOCX / HTML / WebP / TIFF and wants to
  "remove metadata", "scrub EXIF", "strip C2PA", "anonymise" before
  sharing.
- Asks "is this AI-generated?" and wants a forensic check (no
  modification).
- Mentions Claude / Gemini / OpenAI / DeepSeek / Grok watermarks by name.

Do **not** use this skill for:

- Audio / video watermarking (not yet supported — see BACKLOG P4).
- General spell-checking or grammar rewriting (use the built-in tools).
- Content moderation or policy enforcement (out of scope).

## Routing

Match the user request to one of these and delegate:

| User intent                                    | Use                                                            |
|------------------------------------------------|----------------------------------------------------------------|
| Strip ZWSP / homoglyphs from pasted text       | `clean_text` MCP tool, or the VS Code extension's `WatermarkRemover: Clean AI watermarks from selection` command, or `watermarkremover clean-text --stdin` over the CLI |
| Rewrite AI-sounding phrasing (EN or RU)        | `clean_text` with `statistical: true` (or the extension's `watermarkremover.statistical: true` setting + re-run) |
| Strip frontmatter / AI signatures from markdown| `clean_markdown` MCP tool, or `watermarkremover clean-markdown` over the CLI |
| Strip metadata from an image / document        | `clean_file` MCP tool, the VS Code extension's `WatermarkRemover: Strip metadata from selected file(s)` command, or `watermarkremover clean-file <path>` over the CLI |
| Inspect what metadata a file carries           | `inspect_file` MCP tool, or `watermarkremover inspect-file <path>` over the CLI |
| Inpaint a visible logo / watermark             | `clean_image` MCP tool, or `watermarkremover clean-image photo.png -o clean.png` over the CLI (requires LaMa model) |
| Check for watermarks without modifying         | `detect_text` / `detect_markdown` MCP tool, the extension's `WatermarkRemover: Detect AI watermarks in selection` command, or `watermarkremover detect-text --stdin` over the CLI |
| Map a watermark region on an image             | `detect_watermark` MCP tool, or `watermarkremover detect-watermark photo.png` over the CLI |

## VS Code extension

The [WatermarkRemover VS Code extension](../../) (this directory's
parent) adds three commands and two context-menu integrations on top of
the CLI. When the user is in VS Code, prefer the extension commands over
manual CLI invocation — they handle error reporting, status-bar
notifications, and the file-URI plumbing for free.

The three commands:

| Command                                  | When it fires                                                |
|------------------------------------------|--------------------------------------------------------------|
| `watermarkRemover.cleanText`             | Text is selected in the editor → right-click → Clean AI watermarks |
| `watermarkRemover.detectText`            | Text is selected in the editor → right-click → Detect AI watermarks |
| `watermarkRemover.cleanFile`             | A file is selected in the Explorer → right-click → Strip metadata |

The extension's `watermarkremover.binaryPath` setting lets the user
point at a custom binary (e.g. a `dotnet run` wrapper for source-mode
development). The default is `watermarkremover` on `$PATH`.

## MCP server

When the `watermarkremover` MCP server is registered in the host (see
[`docs/MCP.md`](../../../../docs/MCP.md)), the agent gets the full set
of 8 tools: `clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
`detect_text`, `detect_markdown`, `inspect_file`, `detect_watermark`.
Prefer the MCP tools over the CLI when they are available — they're one
round-trip and they don't spawn a subprocess.

The fastest install (works for Claude Code, OpenCode, MiniMax Code,
Cursor, Continue, and any other MCP-compatible host):

```bash
# Release binary on $PATH
claude mcp add watermarkremover -- watermarkremover serve-mcp
# or, from source
claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp
```

The `serve-mcp` command also supports `--transport http --port 5090`
for the Streamable HTTP transport (Docker, remote agents).

## CLI fallback

When neither the extension nor the MCP server is available, the
canonical command shape is:

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

`run.sh` / `run.ps1` wrappers in the per-skill folders under
[`skills/`](../) do exactly that for each format — they pipe stdin to
the right CLI subcommand and print the result.

## Examples

### 1. ZWSP in pasted text

User: "This text from Claude has weird invisible chars — clean it."

Agent: runs the VS Code extension's `cleanText` command (or invokes
`clean_text` MCP tool with `stripInvisible: true` over MCP, or shells
out to `watermarkremover clean-text --stdin` over the CLI). The
selection is replaced with the cleaned text.

### 2. Strip metadata before sharing a photo

User: "I want to upload this JPEG to the forum — strip the EXIF first."

Agent: right-clicks the file in the Explorer → "Strip metadata". The
extension spawns `watermarkremover clean-file` and the file gets a
`photo-clean.jpg` sibling.

### 3. Forensic check (no modification)

User: "Is this email AI-generated? Don't change it."

Agent: selects the text → "Detect AI watermarks" → a new editor tab
opens with the JSON array of matches like
`[{ "vendor": "claude", "kind": "zwsp", "evidence": "…" }]`.

## Error handling

- **Binary not on `$PATH`** — the extension shows an information
  message with the install link. The CLI returns a non-zero exit code
  with a remediation message. The MCP server fails to start with a
  clear `spawn ENOENT`. Point the user at
  [README → Installation](../../../../README.md#-installation).
- **Empty input** — `clean_text` and `clean_markdown` return a tool
  error (`IsError: true`). The extension's `cleanText` command also
  surfaces a "selection is empty" error. Tell the user "Nothing to
  clean."
- **Unsupported file extension** — `clean_file` and `inspect_file`
  return exit code 3 (per the project exit-code convention). The
  extension skips it silently and reports "skipped N (unsupported
  format or non-file URI)". Pick a different command from the routing
  table above.
- **Image without LaMa model** — `clean_image` needs the ONNX model
  downloaded first (`watermarkremover download-model`). Point the user
  at the README.

## Reference

- Per-skill deep-dive: [`docs/SKILLS.md`](../../../../docs/SKILLS.md)
  and the 5 folders under [`skills/`](../).
- VS Code extension: [`vscode/watermark-remover/`](../../).
- VS Code install + config: [`docs/VS-CODE.md`](../../../../docs/VS-CODE.md).
- MCP server reference: [`docs/MCP.md`](../../../../docs/MCP.md).
- Architecture: [`docs/ARCHITECTURE.md`](../../../../docs/ARCHITECTURE.md).
