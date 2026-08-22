---
name: watermark-clean-image
description: >
  Use when the user asks to remove a visual watermark, logo, or overlay
  from an image. Calls the WatermarkRemover `detect_watermark` MCP tool
  first to locate the mask, then `clean_image` to inpaint over it with
  the LaMa ONNX model. Requires the LaMa model to be downloaded once
  (`watermarkremover download-model`).
license: MIT
compatibility: opencode, claude-code, minimax-code, cursor, continue
---

# Skill — `watermark-clean-image`

Pixel-level watermark removal. The pipeline is **not** pixel-preserving —
the watermark region is inpainted with the LaMa (Large Mask
Inpainting) ONNX model. For *metadata-only* cleaning, use
`watermark-clean-file` instead.

## When to use

Activate this skill when the user:

- Shows an image with a visible logo / watermark and asks to "remove
  it" / "clean it" / "erase the watermark"
- Has a screenshot of a paid stock photo and wants the watermark off
- Has a photo with a date stamp, signature, or text overlay to remove
- Wants a transparent overlay removed from a generated image

Do **not** use this skill for:

- EXIF / C2PA / metadata only (use `watermark-clean-file` — that's a
  different pipeline and is much faster)
- Detection only (use `watermark-detect`)

## Prerequisites

The LaMa ONNX model must be present on disk. If it's not, the CLI
prints a clear error and tells the user to run:

```bash
watermarkremover download-model
```

The download is ~200 MB and is a one-time operation. The model is
cached at:

- Linux / macOS: `~/.local/share/WatermarkRemover/lama-big.onnx`
  (or `$XDG_DATA_HOME/WatermarkRemover/`)
- Windows: `%LOCALAPPDATA%\WatermarkRemover\lama-big.onnx`

The model can also be pre-staged in a Docker volume or
`./models/lama-big.onnx` (the CLI checks in this order: `./models/`,
`$WATERMARKREMOVER_MODEL_DIR`, then the OS cache path).

## Tool call

Always detect first — the mask is the input to the inpaint step.

### 1. Detect the watermark region

```json
{
  "name": "detect_watermark",
  "arguments": { "path": "/tmp/photo.png" }
}
```

Response:

```json
{
  "regions": [
    {
      "x": 1080, "y": 1600, "width": 200, "height": 80,
      "confidence": 0.93,
      "kind": "text-overlay"
    }
  ]
}
```

If `regions` is empty, the auto-detector didn't find anything. Fall
back to a manual mask: paint a black-on-white PNG in any image editor
at the watermark's location, save it as `mask.png`, and pass it as
the `maskPath` argument to `clean_image`.

### 2. Inpaint over the detected region

```json
{
  "name": "clean_image",
  "arguments": {
    "path": "/tmp/photo.png",
    "maskPath": null
  }
}
```

The response is the cleaned image as an `ImageContentBlock` with
`image/png` MIME. If the auto-detector found a region, the inpainter
uses it; otherwise pass `maskPath` explicitly.

CLI fallback:

```bash
watermarkremover detect-watermark photo.png --json
watermarkremover clean-image photo.png -o clean.png
watermarkremover clean-image photo.png --mask mask.png -o clean.png
```

## Examples

### 1. Stock-photo watermark in the bottom-right

User: "Remove the 'Getty Images' watermark from this photo."

1. `detect_watermark` → finds a region at (1080, 1600) with 200×80
   size, confidence 0.93, kind `text-overlay`.
2. `clean_image` with no `maskPath` (uses the detected region).
3. Return the cleaned PNG to the user.

### 2. Hand-drawn mask for a tricky case

The auto-detector missed a translucent logo. User says "I can see it,
the agent doesn't". The agent:

1. Tells the user: "I'll need a mask. Please paint the watermark
   white on a black background and save it as `mask.png`, then send
   it back."
2. Calls `clean_image` with `maskPath: "/tmp/mask.png"`.

## Mask guidance

- **White = inpaint, black = keep.** Standard LaMa convention.
- The mask should **only** cover the watermark, with a small margin
  (~10 px) to avoid edge artefacts.
- If the watermark has a soft / semi-transparent edge, the mask should
  be slightly larger to capture the gradient.
- For very large watermarks (>50% of the image), break the job into
  multiple smaller masks — the inpainter quality drops on huge
  inpaint regions.

## Error handling

- **Model not found** — surface the download instruction. Do **not**
  try to install the model automatically; the user should consent to
  the download.
- **`detect_watermark` returns no regions** — switch to manual mask
  mode. Do not retry the detector.
- **Output looks worse than the input** — LaMa inpainting is not
  perfect on busy backgrounds. Try a tighter mask. If that doesn't
  help, fall back to a smaller inpaint region with `--blur-radius` to
  feather the edges.
- **Unsupported format** — LaMa works on PNG / JPEG / WebP. HEIC,
  AVIF, TIFF are not yet supported (see `BACKLOG.md → P1`).

## Reference

- MCP tool reference:
  [docs/MCP.md → `clean_image`](../../docs/MCP.md#clean_image),
  [`detect_watermark`](../../docs/MCP.md#detect_watermark)
- Architecture: [docs/ARCHITECTURE.md → Image cleaning](../../docs/ARCHITECTURE.md#image-cleaning-clean-image)
