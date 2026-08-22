---
name: watermark-clean-markdown
description: >
  Use when the user pastes or references a Markdown file and asks to
  strip AI provenance: frontmatter, AI-specific phrasings, invisible
  characters in prose — while preserving fenced code blocks verbatim.
  Calls the WatermarkRemover `clean_markdown` MCP tool or pipes input
  through `watermarkremover clean-markdown`.
license: MIT
compatibility: opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-clean-markdown`

Strip AI traces from a Markdown document **without mangling the code
inside fenced blocks**. This is the difference between this skill and
`watermark-clean-text`: the markdown cleaner parses the document once,
lifts every fenced block out of the way, runs the 20+ cleanup toggles
over the prose, then drops the code blocks back in unchanged.

## When to use

Activate this skill when the user:

- Pastes a `.md` / `.markdown` document and asks to clean it
- Asks to strip "AI signatures" / "AI watermarks" from a blog post
- Wants frontmatter (YAML / TOML) removed from a document
- Mentions that a markdown file is "forbidden" to be detected as AI
- Wants the prose normalized but the code blocks preserved verbatim

Do **not** use this skill for:

- Plain text without any Markdown structure (use `watermark-clean-text`)
- HTML / DOCX / PDF (use `watermark-clean-file`)
- Code-only files (use `watermark-clean-text` — there is no prose to
  rewrite)
- Detection only (use `watermark-detect`)

## Tool call

```json
{
  "name": "clean_markdown",
  "arguments": {
    "markdown": "---\ntitle: Demo\n---\n# Hello\n\nSome text\u200b here.\n\n```python\nprint('hi')\n```\n",
    "stripAll": true
  }
}
```

Response:

```json
{
  "cleaned": "# Hello\n\nSome text here.\n\n```python\nprint('hi')\n```\n",
  "transformsApplied": [
    "strip-frontmatter",
    "strip-invisible-unicode",
    "strip-em-dashes"
  ]
}
```

If `stripAll: true` is passed, every cleanup toggle is enabled. For
finer control, set individual flags — see
[`docs/CONFIGURATION.md → markdown`](../../docs/CONFIGURATION.md#markdown)
for the full list (21 toggles in WR-S7).

Fallback when MCP is unavailable:

```bash
watermarkremover clean-markdown --in post.md --out post.cleaned.md
# or
cat post.md | watermarkremover clean-markdown --strip-all
```

## Examples

### 1. Strip frontmatter and AI-signature dashes

Before:

```markdown
---
title: Why Rust is the Future
author: AI
date: 2025-01-01
---

# Why Rust is the Future

In today's fast-paced world — software development is evolving — more
and more teams are adopting Rust. It's important to note that this
trend will continue.
```

After (`stripAll: true`):

```markdown
# Why Rust is the Future

In today's fast-paced world, software development is evolving, and more
and more teams are adopting Rust. This trend will continue.
```

### 2. Preserve code blocks untouched

Before:

````markdown
# Setup

Use this snippet:

```rust
fn main() {
    println!("\u200bhello world");
}
```
````

After (only invisible characters inside prose are stripped, the
`println!` line keeps its ZWSP because the user only asked for prose
cleaning — but the `clean-text` skill would have stripped it). If the
user wants code blocks cleaned too, run the cleaned Markdown back
through `watermark-clean-text`.

## Error handling

- **No frontmatter found** — that's fine, the `strip-frontmatter`
  transform is a no-op. The rest still runs.
- **Unclosed fenced block** — the cleaner treats everything after the
  opening fence as code, so transformations stop at EOF. Tell the user
  the file may have been truncated.
- **Output is shorter than expected** — check whether `strip-headings`,
  `strip-links`, or `strip-images` is enabled; they can dramatically
  shrink the doc.

## Language notes

- **English (EN)** — full coverage. The 21 toggles include the most
  common "AI tells" (em-dashes, "It's important to note that",
  "In today's fast-paced world", etc.).
- **Russian (RU)** — frontmatter and invisible-char stripping work
  identically. The prose rewrite is conservative and won't damage real
  Russian phrasing.
- **CJK / RTL** — Unicode hygiene (Layer A) works on any script; the
  prose rewrites are EN/RU only.

## Reference

- MCP tool reference:
  [docs/MCP.md → `clean_markdown`](../../docs/MCP.md#clean_markdown)
- Config keys:
  [`docs/CONFIGURATION.md → markdown`](../../docs/CONFIGURATION.md#markdown)
- CLI reference: `watermarkremover clean-markdown --help`
