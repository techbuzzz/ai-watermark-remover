---
description: Strip metadata (EXIF, XMP, IPTC, C2PA, ICC) from a file
---

The user wants to scrub metadata from a file before sharing. Use the
WatermarkRemover `clean_file` MCP tool.

# Input

The path to the file follows the command as `$ARGUMENTS`. If
`$ARGUMENTS` is empty, ask the user to provide a file path. The path
may be relative to the project root.

# Tool call

```json
{
  "name": "clean_file",
  "arguments": {
    "path": "$ARGUMENTS"
  }
}
```

The tool reads the file, dispatches by extension to the right
metadata cleaner (JPEG / PNG / PDF / DOCX / HTML / WebP / TIFF /
HEIF / AVIF / EPUB / PPTX / XLSX / RTF), and writes the cleaned
output. For images, the **pixel data is preserved**; only metadata
chunks are removed.

# Fallback (no MCP)

```bash
watermarkremover clean-file "$ARGUMENTS"
# or, recursively, for a whole folder
watermarkremover clean-file "$ARGUMENTS" --recursive
```

The CLI writes a cleaned copy alongside the original with a
`-clean` suffix before the extension (e.g. `photo.jpg` →
`photo-clean.jpg`).

# What to report

Reply with a short summary:

> Cleaned file: photo-clean.jpg
> Format: JPEG
> Removed: EXIF (camera, GPS), XMP, ICC profile, IPTC.
> Original size: 3.2 MB → cleaned: 2.8 MB.

If the file is unsupported, the tool returns a 400 with the list of
supported extensions — pick the right per-format skill or convert
the file first.

# Don't

- Do **not** call `clean_file` on a non-file path (directories need
  `--recursive`; the MCP tool handles this automatically when given
  a directory).
- Do **not** call this for inpainting visual watermarks — that's
  `clean_image` (no slash command), and it requires the LaMa model.
- Do **not** assume the file is an image — the router supports PDF,
  DOCX, and HTML too.

# See also

- `/wr-detect` — inspect what metadata a file carries without
  modifying it (`inspect_file` MCP tool).
- [`docs/MCP.md` → `clean_file`](../../../docs/MCP.md#clean_file)
  for the full parameter reference.
