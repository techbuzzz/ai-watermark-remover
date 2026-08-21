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
  `detect_markdown` / `inspect_file` / `detect_watermark` tools. The
  project also ships three OpenCode slash commands (`/wr-clean-text`,
  `/wr-clean-file`, `/wr-detect`) for one-shot invocation.
license: MIT
compatibility: opencode
---

# Skill — `watermark-remover`

Master skill for the WatermarkRemover integration. This is the entry
point: it teaches the OpenCode agent **when** to think about
WatermarkRemover and points at the right tool / slash command / CLI
invocation. The five per-format skills (clean-text, clean-markdown,
clean-file, clean-image, detect) live in [`skills/`](../../../skills/)
and have the deep per-format reference.

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
| Strip ZWSP / homoglyphs from pasted text       | `clean_text` MCP tool, or `/wr-clean-text <text>` slash cmd    |
| Rewrite AI-sounding phrasing (EN or RU)        | `clean_text` with `statistical: true`                          |
| Strip frontmatter / AI signatures from markdown| `clean_markdown` MCP tool (no slash command)                   |
| Strip metadata from an image / document        | `clean_file` MCP tool, or `/wr-clean-file <path>` slash cmd    |
| Inspect what metadata a file carries           | `inspect_file` MCP tool                                        |
| Inpaint a visible logo / watermark             | `clean_image` MCP tool (no slash command)                      |
| Check for watermarks without modifying         | `detect_text` / `detect_markdown` MCP tool, or `/wr-detect`   |
| Map a watermark region on an image             | `detect_watermark` MCP tool                                    |

## Slash commands (auto-discovered)

This skill ships three slash commands in `.opencode/commands/`:

- **`/wr-clean-text <text>`** — strip invisible characters and AI
  watermarks from the given text. Equivalent to calling
  `clean_text` with `stripInvisible: true`. The text is the
  `$ARGUMENTS` after the command name.
- **`/wr-clean-file <path>`** — strip metadata (EXIF, XMP, IPTC, C2PA,
  ICC, …) from the file at the given path. Writes a cleaned copy
  alongside the original (suffix `-clean` before the extension).
- **`/wr-detect <text>`** — run detection only, return the watermark
  matches (vendor, kind, evidence). Use this when the user wants a
  forensic check, not a cleanup.

## MCP server

When the `watermarkremover` MCP server is registered (see
[`docs/MCP.md → Install → OpenCode`](../../../docs/MCP.md#opencode)),
the agent gets the full set of 8 tools: `clean_text`,
`clean_markdown`, `clean_file`, `clean_image`, `detect_text`,
`detect_markdown`, `inspect_file`, `detect_watermark`. Prefer the MCP
tools over the CLI when they are available — they're one round-trip
and they don't spawn a subprocess.

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

Agent: invokes `/wr-clean-text "Hello world"` (or
`clean_text` MCP tool with `stripInvisible: true`), gets back
`"Helloworld"` plus a `removed` list naming `ZWSP x2`.

### 2. Strip metadata before sharing a photo

User: "I want to upload this JPEG to the forum — strip the EXIF first."

Agent: invokes `/wr-clean-file ./photo.jpg`, gets back the path of
the cleaned file (`./photo-clean.jpg`).

### 3. Forensic check (no modification)

User: "Is this email AI-generated? Don't change it."

Agent: invokes `/wr-detect <text>`, returns a JSON array of matches
like `[{ "vendor": "claude", "kind": "zwsp", "evidence": "…" }]`.

## Error handling

- **Binary not on PATH** — the MCP server entry in `opencode.jsonc`
  has `enabled: false` by default. Tell the user to install the
  `watermarkremover` binary (see [README → Installation](../../../README.md#-installation))
  and flip `enabled: true`.
- **Empty input** — `clean_text` and `clean_markdown` return a tool
  error (`IsError: true`). Tell the user "Nothing to clean."
- **Unsupported file extension** — `clean_file` and `inspect_file`
  return a 400 with the supported extension list. Pick a different
  command from the routing table above.
- **Image without LaMa model** — `clean_image` needs the ONNX model
  downloaded first (`watermarkremover download-model`). Point the user
  at the README.

## Reference

- Per-skill deep-dive: [`docs/SKILLS.md`](../../../docs/SKILLS.md) and
  the 5 folders under [`skills/`](../../../skills/).
- MCP server reference: [`docs/MCP.md`](../../../docs/MCP.md).
- Architecture: [`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md).
