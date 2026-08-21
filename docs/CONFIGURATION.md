# ⚙️ Configuration reference

> The canonical example lives in [`src/config.yaml`](../src/config.yaml).
> This page documents every key, its default, and the layers it controls.

## Resolution order

When the CLI starts up, `ConfigLoader` resolves the active configuration
in this order (first match wins):

1. `--config <path>` (CLI flag) — explicit user override
2. `./config.yaml` in the current working directory
3. `config.yaml` next to the apphost (`/usr/local/bin/config.yaml`,
   `C:\Tools\config.yaml`, etc.)
4. **Built-in defaults** compiled into the binary

**Unknown keys are ignored** so adding new sections is a non-breaking
change. CLI flags always override the resolved value.

> **Note:** environment-variable overrides (`WATERMARKREMOVER__TEXT__STATISTICAL=true`,
> double-underscore notation à la ASP.NET) are on the
> [BACKLOG.md → P2](./BACKLOG.md#p2--platform--ux-v1x) list.

## Top-level shape

```yaml
text:       # Layer A / B / C toggles + LLM back-translation
markdown:   # MarkdownCleaner toggle set
image:      # LaMa model path + mask heuristics
metadata:   # JPEG / PNG / PDF / DOCX / HTML cleaner defaults
logging:    # Serilog sinks + minimum level
```

---

## `text`

Controls the text-cleaning pipeline.

| Key                          | Type      | Default                                | Description |
|------------------------------|-----------|----------------------------------------|-------------|
| `text.layers.unicode`        | `bool`    | `true`                                 | Enable **Layer A** — strip invisible Unicode code points, apply NFKC, fold Latin homoglyphs. Safe for Cyrillic. |
| `text.layers.statistical`    | `bool`    | `false`                                | Enable **Layer B** — swap green-list tokens for synonyms via the built-in EN+RU dictionary. Optionally back-translates through an LLM. |
| `text.layers.vendor_specific` | `bool`    | `true`                                 | Enable **Layer C** — Claude / Gemini / OpenAI vendor heuristics. |
| `text.llm_endpoint`          | `string`  | `"http://localhost:11434"`             | Ollama-compatible endpoint URL for Layer B back-translation. Ignored when `layers.statistical = false` or no LLM call is triggered. |
| `text.llm_model`             | `string`  | `"llama3"`                             | Model name to request from the LLM endpoint. |

### Example

```yaml
text:
  layers:
    unicode: true
    statistical: true
    vendor_specific: true
  llm_endpoint: "http://localhost:11434"
  llm_model: "llama3.1:8b"
```

---

## `markdown`

Twenty-plus toggleable transforms applied by `MarkdownCleaner`. The
default profile strips AI-specific artifacts but leaves the document
structure intact.

| Key                          | Type      | Default | Description |
|------------------------------|-----------|---------|-------------|
| `markdown.strip_headings`    | `bool`    | `true`  | Strip `#`-prefixed headings. |
| `markdown.strip_code_fences` | `bool`    | `false` | Strip fenced code blocks (```…```). **Ignored** when `preserve_code_blocks = true`. |
| `markdown.strip_inline_code` | `bool`    | `false` | Strip backtick-wrapped inline code. |
| `markdown.strip_links`       | `bool`    | `false` | Strip `[text](url)` links. |
| `markdown.strip_images`      | `bool`    | `true`  | Strip `![alt](src)` images. |
| `markdown.strip_html`        | `bool`    | `true`  | Strip raw `<tag>…</tag>` blocks. |
| `markdown.strip_frontmatter` | `bool`    | `true`  | Strip YAML frontmatter at the top of the document. |
| `markdown.strip_ai_signatures` | `bool`  | `true`  | Strip "As an AI language model…", emoji sign-offs, etc. |
| `markdown.strip_mentions`    | `bool`    | `true`  | Strip `@user` mentions. |
| `markdown.strip_unicode_md`  | `bool`    | `true`  | Strip invisible Unicode code points (same as text Layer A). |
| `markdown.strip_trailing_ws` | `bool`    | `true`  | Strip trailing whitespace per line. |
| `markdown.preserve_code_blocks` | `bool` | `true`  | **Force** preserve fenced code blocks regardless of other settings. |

### Example

```yaml
markdown:
  strip_headings: false         # keep the structure
  strip_code_fences: false
  strip_inline_code: false
  strip_links: false
  strip_images: true            # but kill embedded images
  strip_html: true
  strip_frontmatter: true
  strip_ai_signatures: true
  strip_mentions: true
  strip_unicode_md: true
  strip_trailing_ws: true
  preserve_code_blocks: true
```

Use the `--strip-all` CLI flag to enable *every* transform at once
(effectively a "kill everything but the prose" mode).

---

## `image`

| Key                            | Type     | Default                                       | Description |
|--------------------------------|----------|-----------------------------------------------|-------------|
| `image.model_path`             | `string` | `"./models/big_lama_regular_inpaint.onnx"`    | Path to the LaMa ONNX model. The file is downloaded by `watermarkremover download-model`. |
| `image.auto_detect_threshold`  | `float`  | `0.4`                                         | Sensitivity for the mask generator's colour-frequency heuristic. Range 0.0–1.0. Lower = more aggressive (more pixels flagged as watermark). |
| `image.blend_edges`            | `bool`   | `true`                                        | Alpha-blend the inpainted patch back into the source image. Improves anti-aliased edges; disable for forensic comparison. |

### Example

```yaml
image:
  model_path: "/opt/models/big_lama.onnx"
  auto_detect_threshold: 0.5
  blend_edges: true
```

---

## `metadata`

| Key                          | Type      | Default | Description |
|------------------------------|-----------|---------|-------------|
| `metadata.strip_c2pa`        | `bool`    | `true`  | Strip C2PA / Content Credentials manifests. |
| `metadata.strip_exif`        | `bool`    | `true`  | Strip EXIF (camera, GPS, timestamps). |
| `metadata.strip_xmp`         | `bool`    | `true`  | Strip XMP (Adobe / IPTC XMP). |
| `metadata.preserve_color_profile` | `bool` | `true` | Preserve the ICC colour profile (when present). |

> **Note:** `strip_iptc` and `strip_maker_notes` are recognised by the
> JPEG cleaner but **not** yet surfaced in `config.yaml` — see
> [BACKLOG.md → P1](./BACKLOG.md#metadata-cleaner-enhancements). For
> now they follow `strip_exif`.

### Example

```yaml
metadata:
  strip_c2pa: true
  strip_exif: true
  strip_xmp: true
  preserve_color_profile: true
```

---

## `logging`

Drives the Serilog configuration in `Program.cs`.

| Key                | Type     | Default        | Description |
|--------------------|----------|----------------|-------------|
| `logging.level`    | `string` | `"Information"` | One of: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |
| `logging.output`   | `string` | `"console"`     | Comma-separated list of sinks: `console`, `file`. The `file` sink writes to `logs/watermarkremover-<date>.log` (rolling daily). |

### Example

```yaml
logging:
  level: "Debug"
  output: "console,file"
```

---

## Full example

A complete `config.yaml` with every key set explicitly:

```yaml
text:
  layers:
    unicode: true
    statistical: false
    vendor_specific: true
  llm_endpoint: "http://localhost:11434"
  llm_model: "llama3"

markdown:
  strip_headings: true
  strip_code_fences: false
  strip_inline_code: false
  strip_links: false
  strip_images: true
  strip_html: true
  strip_frontmatter: true
  strip_ai_signatures: true
  strip_mentions: true
  strip_unicode_md: true
  strip_trailing_ws: true
  preserve_code_blocks: true

image:
  model_path: "./models/big_lama_regular_inpaint.onnx"
  auto_detect_threshold: 0.4
  blend_edges: true

metadata:
  strip_c2pa: true
  strip_exif: true
  strip_xmp: true
  preserve_color_profile: true

logging:
  level: "Information"
  output: "console"
```

---

## Validation

`ConfigLoader` is **lenient** today — unknown keys are ignored, and
invalid values fall back to defaults silently. Strict validation (fail
fast on unknown keys) is on the
[BACKLOG.md → P2](./BACKLOG.md#p2--platform--ux-v1x) list. Until then,
use `--verbose` to see which keys were applied.
