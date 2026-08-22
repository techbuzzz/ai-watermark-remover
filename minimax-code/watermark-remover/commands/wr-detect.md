---
description: Detect (without modifying) AI watermarks in text
---

The user wants a forensic check, not a cleanup. Use the WatermarkRemover
`detect_text` MCP tool — it returns matches **without** modifying the
input.

# Input

The user's text follows the command as `$ARGUMENTS`. If `$ARGUMENTS`
is empty, ask the user to paste the text.

# Tool call

```json
{
  "name": "detect_text",
  "arguments": {
    "text": "$ARGUMENTS"
  }
}
```

# Result shape

The tool returns a JSON array of `WatermarkMatch` objects:

```json
[
  {
    "vendor": "claude",
    "kind": "zwsp",
    "evidence": "U+200B x4 between words 2 and 3"
  },
  {
    "vendor": "openai",
    "kind": "homoglyph",
    "evidence": "Cyrillic 'а' at position 47"
  }
]
```

| Field      | Meaning                                                                  |
|------------|--------------------------------------------------------------------------|
| `vendor`   | `claude`, `gemini`, `openai`, `deepseek`, `grok`, `unknown`              |
| `kind`     | `zwsp`, `homoglyph`, `soft-hyphen`, `ligature`, `bom`, `bidi-override`   |
| `evidence` | human-readable explanation of what was found and where                   |

# Fallback (no MCP)

```bash
printf '%s' "$ARGUMENTS" | watermarkremover detect-text --stdin
# or
watermarkremover detect-text "$ARGUMENTS"
```

# What to report

If the array is **empty**, tell the user:

> No AI watermarks detected. The text may still be AI-generated — these
> are heuristic checks, not a guarantee.

If the array has **matches**, give a short human-readable summary:

> Detected 2 watermark signatures:
> - Claude — ZWSP x4 between "Hello" and "world"
> - OpenAI — Cyrillic 'а' at position 47 (homoglyph)

Do not paraphrase the `evidence` field — the user can see it in the
JSON if they want detail.

# Don't

- Do **not** modify the input text. The whole point of `/wr-detect` is
  the user wants a read-only check. If they want cleanup, point them at
  `/wr-clean-text`.
- Do **not** report a single match as "AI-generated" with high
  confidence. The heuristics are per-vendor and can have false
  positives (especially homoglyphs in legitimate Cyrillic / mixed
  text). Be specific about what was found.
- Do **not** use this on markdown — use `detect_markdown` (no slash
  command) which also checks for frontmatter and AI signatures.
