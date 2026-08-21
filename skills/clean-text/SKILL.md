---
name: watermark-clean-text
description: >
  Use when the user pastes AI-generated text and asks to clean it, remove
  invisible characters, strip ZWSP / soft hyphens, normalise homoglyphs, or
  rewrite AI-sounding phrasing. Calls the WatermarkRemover `clean_text` MCP
  tool (preferred) or pipes input through `watermarkremover clean-text`.
license: MIT
compatibility: opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-clean-text`

Teach the agent to use WatermarkRemover's text-cleaning pipeline. The
pipeline runs three independent layers (Unicode hygiene → statistical
rewrite → vendor-specific detectors) and returns the cleaned text plus a
short report of what was removed.

## When to use

Activate this skill when the user:

- Pastes a block of text that "looks weird" or has stray invisible chars
- Asks to "clean", "normalise", "strip watermarks", or "remove AI traces"
  from text
- Mentions `ZWSP`, `U+200B`, zero-width characters, homoglyphs, or
  Cyrillic / Latin look-alikes
- Wants text rewritten in plainer language (EN or RU)
- Asks "is this AI-generated?" and wants a check

Do **not** use this skill for:

- Markdown / code blocks (use `watermark-clean-markdown` instead — it
  preserves fenced code, this skill will mangle it)
- Files (use `watermark-clean-file`)
- Images (use `watermark-clean-image`)
- "Just check, don't modify" — use `watermark-detect`

## Tool call

Prefer the MCP tool — the agent already has it when the `watermarkremover`
MCP server is registered (see [docs/MCP.md](../../docs/MCP.md)):

```json
{
  "name": "clean_text",
  "arguments": {
    "text": "Hello\u200bWorld",
    "stripInvisible": true,
    "statistical": false
  }
}
```

The response is a JSON object:

```json
{
  "cleaned": "HelloWorld",
  "removed": [
    { "kind": "ZWSP", "codePoint": "U+200B", "count": 1 }
  ]
}
```

`stripInvisible` runs Layer A (Unicode hygiene). `statistical` (default
`false`) runs Layer B — the synonym dictionary (EN + RU) and optional
LLM back-translation. Leave it `false` when the user only asked to strip
invisible chars; flip it on when the user asked to "rewrite" or "make it
sound more human".

If MCP is not available, fall back to the CLI:

```bash
echo "HelloWorld" | watermarkremover clean-text --stdin
# or
watermarkremover clean-text "Hello\u200bWorld"
```

The `run.sh` / `run.ps1` wrappers in this folder do exactly that — pipe
stdin through the CLI, print the cleaned text on stdout, the report on
stderr.

## Examples

### 1. Strip zero-width spaces from pasted text (English)

User pastes:

> "Hello\u200b\u200bWorld — this is a\u200d test."

Agent invokes `clean_text` with `stripInvisible: true` and gets back:

> "HelloWorld — this is a test."

Plus a report naming `ZWSP` (2) and `ZWJ` (1).

### 2. Rewrite AI-sounding phrasing (Russian)

User pastes:

> "Это значимый результат, который мы использовали в проекте."

Agent invokes `clean_text` with `stripInvisible: true, statistical: true`:

> "Это существенный результат, который мы применили в проекте."

(`значимый` → `существенный`, `использовали` → `применили` from the built-in
RU synonym dictionary.)

### 3. Cyrillic / Latin homoglyph detection

If the text mixes Latin letters with Cyrillic look-alikes (e.g. Cyrillic
`а`/`е`/`о` between Latin chars), the cleaned output will fold them back
to Latin **only** when they sit between Latin neighbours — so the
Russian word «Привет» stays untouched, but `Пpивeт` with mixed
alphabets gets normalised.

## Error handling

- **Empty input** — `clean_text` returns a tool error (`IsError: true`).
  Tell the user "Nothing to clean — the input was empty."
- **MCP not registered** — fall back to the CLI wrapper. If the
  `watermarkremover` binary is not on `PATH`, point the user at
  [docs/MCP.md](../../docs/MCP.md) or
  [README → Installation](../../README.md#-installation).
- **Output differs from the input in surprising ways** — the user may
  not have asked for statistical rewriting. Check whether `statistical`
  was set; if it was off and the output still changed, it's Layer A
  (Unicode normalisation), which is always safe.

## Language notes

- **English (EN)** — full coverage; synonym dictionary has ~400 entries
  for common "AI-tell" phrasing.
- **Russian (RU)** — first-class support; synonym dictionary has ~200
  entries, Cyrillic homoglyph folding is conservative (only fires between
  Latin neighbours, so real Russian is never damaged).
- Other languages — Layer A (Unicode hygiene) works on any script; Layer
  B has no dictionary for non-EN/RU text and is a no-op for them.

## Reference

- MCP tool reference: [docs/MCP.md → `clean_text`](../../docs/MCP.md#clean_text)
- CLI reference: `watermarkremover clean-text --help`
- Architecture: [docs/ARCHITECTURE.md → Text cleaning](../../docs/ARCHITECTURE.md#text-cleaning-clean-text)
