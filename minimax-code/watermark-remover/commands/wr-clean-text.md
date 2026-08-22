---
description: Strip invisible characters and AI watermarks from text
---

The user wants to clean text. Use the WatermarkRemover `clean_text`
MCP tool. Call it with `stripInvisible: true` by default — the user
will explicitly ask for "rewrite" / "humanise" if they want
`statistical: true`.

# Input

The user's text follows the command as `$ARGUMENTS`. If `$ARGUMENTS`
is empty, ask the user to paste the text.

# Tool call

```json
{
  "name": "clean_text",
  "arguments": {
    "text": "$ARGUMENTS",
    "stripInvisible": true,
    "statistical": false
  }
}
```

# Fallback (no MCP)

Pipe the text through the CLI:

```bash
printf '%s' "$ARGUMENTS" | watermarkremover clean-text --stdin
```

Or inline:

```bash
watermarkremover clean-text "$ARGUMENTS"
```

# What to report

Reply with the `cleaned` field. If the user asked for a verbose
report (they said "show what you removed" or similar), include the
`removed` array as a short list:

> Cleaned: "HelloWorld" — removed 2 ZWSP, 1 soft hyphen.

If the tool returns an `IsError: true`, the input was empty or
malformed — tell the user "Nothing to clean — please paste the text
you want me to clean."

# Don't

- Do **not** set `statistical: true` unless the user explicitly asked
  to rewrite / humanise / make it sound less AI-like. Layer B changes
  phrasing and the user may not want that.
- Do **not** strip non-ASCII punctuation. Only invisible / zero-width
  code points are safe to remove.
- Do **not** apply this to markdown with fenced code blocks — use
  `clean_markdown` (no slash command) which preserves code.
