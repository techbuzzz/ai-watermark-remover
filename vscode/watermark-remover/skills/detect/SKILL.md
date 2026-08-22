---
name: watermark-detect
description: >
  Use when the user wants to *check* whether text or markdown contains
  AI provenance watermarks, without modifying the input. Calls the
  WatermarkRemover `detect_text` / `detect_markdown` / `detect_watermark`
  MCP tools (or the matching `detect-text` / `detect-markdown` /
  `detect-watermark` CLI commands) and returns a structured report.
license: MIT
compatibility: opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-detect`

Read-only detection. This skill never modifies the input — it returns
a structured `WatermarkMatch[]` report (vendor, kind, evidence, byte
offset) so the user can decide what to do.

## When to use

Activate this skill when the user:

- Asks "is this AI-generated?" / "is this from Claude / Gemini / OpenAI?"
- Wants a forensic report on a pasted block of text or markdown
- Wants to know *where* in a document a watermark sits, before deciding
  whether to clean it
- Wants to audit a corpus of files for AI provenance without touching
  them

Do **not** use this skill for:

- Actually cleaning / rewriting the content (use
  `watermark-clean-text` / `watermark-clean-markdown`)
- Image visual watermarks (use `watermark-clean-image` with the
  `detect_watermark` sub-step — that pipeline has its own auto-mask
  generator)

## Tool calls

### 1. Text

```json
{
  "name": "detect_text",
  "arguments": { "text": "Hello\u200bWorld" }
}
```

Response:

```json
{
  "matches": [
    {
      "vendor": "claude",
      "kind": "invisible-unicode",
      "codePoint": "U+200B",
      "count": 1,
      "evidence": "Zero-Width Space detected in input"
    }
  ]
}
```

### 2. Markdown

```json
{
  "name": "detect_markdown",
  "arguments": {
    "markdown": "---\ntitle: Demo\n---\n# Hello\n"
  }
}
```

Response:

```json
{
  "matches": [
    {
      "vendor": "generic",
      "kind": "yaml-frontmatter",
      "count": 1,
      "evidence": "Document starts with YAML frontmatter"
    }
  ]
}
```

### 3. Image

For visual watermarks, the tool is `detect_watermark` (see
[`watermark-clean-image`](../clean-image/SKILL.md) for details). It
returns regions with `x, y, width, height, confidence, kind`.

## Interpreting the result

| Vendor       | What it means                                                  |
|--------------|----------------------------------------------------------------|
| `claude`     | Anthropic Claude — invisible Unicode (ZWSP runs), token-level biases |
| `gemini`     | Google Gemini — SynthID-style character substitution           |
| `openai`     | OpenAI ChatGPT — token-frequency fingerprint                    |
| `deepseek`   | DeepSeek — (planned, see `BACKLOG.md → P1`)                    |
| `grok`       | xAI Grok — (planned)                                            |
| `mistral`    | Mistral AI — (planned)                                          |
| `generic`    | Not vendor-specific — frontmatter, em-dash patterns, etc.      |

| `kind`                  | What it means                                       |
|-------------------------|-----------------------------------------------------|
| `invisible-unicode`     | ZWSP / ZWJ / soft hyphen / homoglyph                |
| `token-bias`            | Repetitive token patterns characteristic of vendor  |
| `yaml-frontmatter`      | Document starts with YAML / TOML frontmatter        |
| `em-dash-frequency`     | Suspiciously high em-dash / en-dash density         |
| `signature-phrase`      | "It's important to note that", "In today's fast-paced world" |

Each match carries `evidence` — a human-readable explanation. Show the
`evidence` text to the user so they understand *why* the detector fired;
don't just print "yes it's AI".

## Examples

### 1. Quick AI check on a pasted paragraph

User: "Did I just paste AI text? Check it."

```json
{ "name": "detect_text", "arguments": { "text": "..." } }
```

If any matches come back, list them with their `vendor`, `kind`, and
`evidence`. If empty, say "No AI watermarks detected in the supplied
text" — but be careful: an empty result is *not* proof the text is
human-written; detectors have false negatives.

### 2. Audit a corpus of files

```bash
for f in corpus/*.md; do
  echo "=== $f ==="
  watermarkremover detect-markdown -i "$f" --json
done
```

Or, in the agent, iterate the file list and call `detect_markdown` on
each.

## Confidence & false positives

- **False positive risks** — `em-dash-frequency` fires on any text with
  a lot of em-dashes (some technical writing, translated literature).
  Cross-check with other kinds.
- **False negatives** — adversarial rewriting defeats every detector.
  A clean detector result is necessary but not sufficient evidence of
  human authorship.
- **No matches ≠ safe** — never claim the input is "definitely not AI".
  Say "no AI watermarks detected in the supplied input".

## Error handling

- **Empty input** — returns an empty `matches` array, not an error.
  Tell the user there is nothing to detect.
- **Detector throws** — surface the error verbatim; don't try to
  recover. The detection layer is supposed to be robust to bad input
  and any throw is a real bug.

## Reference

- MCP tool reference:
  [docs/MCP.md → `detect_text`](../../docs/MCP.md#detect_text),
  [`detect_markdown`](../../docs/MCP.md#detect_markdown),
  [`detect_watermark`](../../docs/MCP.md#detect_watermark)
- Architecture: [docs/ARCHITECTURE.md → Layer C](../../docs/ARCHITECTURE.md#text-cleaning-clean-text)
- Vendor detector list: `BACKLOG.md → P1`
