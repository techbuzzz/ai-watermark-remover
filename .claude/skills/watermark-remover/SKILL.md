---
name: watermark-remover
description: >
  Use when the user pastes AI-generated text, asks to strip invisible
  characters / ZWSP / Cyrillic homoglyphs, wants to clean metadata from
  a JPEG / PNG / PDF / DOCX / HTML / WebP file before sharing, or wants to
  know whether a text blob carries a Claude / Gemini / OpenAI / DeepSeek /
  Grok watermark. Routes to the `watermarkremover` CLI or — when the MCP
  server is registered (see docs/MCP.md) — to the matching `clean_text` /
  `clean_file` / `clean_markdown` / `clean_image` / `detect_text` /
  `detect_markdown` / `inspect_file` / `detect_watermark` tools.
license: MIT
compatibility: claude-code
---

# Skill — `watermark-remover`

Master skill for the WatermarkRemover integration. This is the entry
point: it teaches the Claude Code agent **when** to think about
WatermarkRemover and points at the right tool / CLI invocation. The
five per-format skills (clean-text, clean-markdown, clean-file,
clean-image, detect) live in [`skills/`](../../../skills/) and have
the deep per-format reference.

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
| Strip ZWSP / homoglyphs from pasted text       | `clean_text` MCP tool                                          |
| Rewrite AI-sounding phrasing (EN or RU)        | `clean_text` with `statistical: true`                          |
| Strip frontmatter / AI signatures from markdown| `clean_markdown` MCP tool                                      |
| Strip metadata from an image / document        | `clean_file` MCP tool                                          |
| Inspect what metadata a file carries           | `inspect_file` MCP tool                                        |
| Inpaint a visible logo / watermark             | `clean_image` MCP tool                                         |
| Check for watermarks without modifying         | `detect_text` / `detect_markdown` MCP tool                      |
| Map a watermark region on an image             | `detect_watermark` MCP tool                                    |

## Slash-style invocation

Claude Code does not auto-discover slash-command files the way OpenCode
does. The closest equivalent is the `clean_text` / `clean_file` /
`clean_image` MCP tools themselves — Claude invokes them as ordinary
tool calls when the user asks. If the MCP server is not registered,
fall back to the CLI (see below). For copy-paste, the user can also
run the per-skill `run.sh` / `run.ps1` wrappers manually.

## MCP server

When the `watermarkremover` MCP server is registered (see
[`docs/CLAUDE-CODE.md`](../../../docs/CLAUDE-CODE.md) and
[`docs/MCP.md → Install → Claude Code`](../../../docs/MCP.md#claude-code)),
the agent gets the full set of 8 tools: `clean_text`,
`clean_markdown`, `clean_file`, `clean_image`, `detect_text`,
`detect_markdown`, `inspect_file`, `detect_watermark`. Prefer the MCP
tools over the CLI when they are available — they're one round-trip
and they don't spawn a subprocess.

The fastest install:

```bash
# Release binary on $PATH
claude mcp add watermarkremover -- watermarkremover serve-mcp

# From source
claude mcp add watermarkremover -- dotnet run --project src/WatermarkRemover.CLI -- serve-mcp
```

Verify with `claude mcp list` — the `watermarkremover` row should
show `connected` once the agent starts.

## CLI fallback

When the MCP server is not registered (or the agent explicitly wants
to shell out), the canonical command shape is:

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
[`skills/`](../../../skills/) do exactly that for each format — they
pipe stdin to the right CLI subcommand and print the result.

## Examples

### 1. ZWSP in pasted text

User: "This text from Claude has weird invisible chars — clean it."

Agent: invokes the `clean_text` MCP tool with
`stripInvisible: true` on the pasted text, gets back
`"Helloworld"` plus a `removed` list naming `ZWSP x2`.

### 2. Strip metadata before sharing a photo

User: "I want to upload this JPEG to the forum — strip the EXIF first."

Agent: invokes `clean_file` with `input_path: "./photo.jpg"`. The
tool returns an `EmbeddedResourceBlock` carrying the cleaned bytes
plus a JSON summary — the agent forwards the cleaned image to the
user.

### 3. Forensic check (no modification)

User: "Is this email AI-generated? Don't change it."

Agent: invokes `detect_text` with the email body, returns a JSON
array of matches like
`[{ "vendor": "claude", "kind": "zwsp", "evidence": "…" }]`.

## Error handling

- **Server not connected** — `claude mcp list` shows nothing for
  `watermarkremover`. Tell the user to install the binary (see
  [README → Installation](../../../README.md#-installation)) and run
  the `claude mcp add` one-liner above.
- **Empty input** — `clean_text` and `clean_markdown` return a tool
  error (`IsError: true`). Tell the user "Nothing to clean."
- **Unsupported file extension** — `clean_file` and `inspect_file`
  return a 400 with the supported extension list. Pick a different
  command from the routing table above.
- **Image without LaMa model** — `clean_image` needs the ONNX model
  downloaded first (`watermarkremover download-model`). Point the user
  at the README.

## Hooks (optional)

A drop-in `hooks.json` snippet that auto-cleans pasted text via
`clean_text` is shipped alongside this skill at
[`.claude/skills/watermark-remover/hooks.json`](./hooks.json). Copy it
into the project's `.claude/settings.json` (merge with the existing
`hooks` key) to enable. See
[`docs/CLAUDE-CODE.md → Auto-clean pasted text`](../../../docs/CLAUDE-CODE.md#auto-clean-pasted-text-optional)
for the exact merge shape.

## Reference

- Per-skill deep-dive: [`docs/SKILLS.md`](../../../docs/SKILLS.md) and
  the 5 folders under [`skills/`](../../../skills/).
- Claude Code install + hook reference: [`docs/CLAUDE-CODE.md`](../../../docs/CLAUDE-CODE.md).
- MCP server reference: [`docs/MCP.md`](../../../docs/MCP.md).
- Architecture: [`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md).
