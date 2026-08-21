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
server:     # HTTP API host knobs (rate-limit, etc.) — `serve` only
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

Every public toggle on `MarkdownCleanOptions` is reachable from
`config.yaml`. The default profile strips AI-specific artifacts but
leaves the document structure intact.

| Key                          | Type      | Default | Description |
|------------------------------|-----------|---------|-------------|
| `markdown.strip_headings`    | `bool`    | `true`  | Strip `#`-prefixed headings. |
| `markdown.strip_code_fences` | `bool`    | `false` | Drop the ``` fence markers (content preserved). **Ignored** when `preserve_code_blocks = true`. |
| `markdown.strip_inline_code` | `bool`    | `false` | Unwrap backtick-wrapped inline code spans. |
| `markdown.strip_links`       | `bool`    | `false` | Unwrap `[text](url)` links (text kept, URL dropped). |
| `markdown.strip_images`      | `bool`    | `true`  | Strip `![alt](src)` images. |
| `markdown.strip_bold_italic` | `bool`    | `false` | Unwrap `**bold**` / `*italic*` / `__bold__` / `_italic_` markers. |
| `markdown.strip_blockquotes` | `bool`    | `false` | Drop the leading `> ` blockquote marker on each line. |
| `markdown.strip_hr`          | `bool`    | `true`  | Drop horizontal-rule lines (`---`, `***`, `___`). |
| `markdown.strip_html`        | `bool`    | `true`  | Strip raw `<tag>…</tag>` HTML blocks. |
| `markdown.strip_comments`    | `bool`    | `true`  | Strip `<!-- … -->` and `[//]: # (…)` comments. |
| `markdown.strip_task_lists`  | `bool`    | `false` | Convert `- [ ] todo` → `- todo`; drop empty task items entirely. |
| `markdown.strip_table_syntax`| `bool`    | `false` | Rewrite `\| a \| b \|` rows to plain-text columns; drop `\| --- \| --- \|` separator rows. |
| `markdown.normalize_lists`   | `bool`    | `true`  | Convert `* item` / `+ item` bullet markers to `- item`. |
| `markdown.unwrap_empty_lists`| `bool`    | `true`  | Drop list items that have no content after the bullet. |
| `markdown.strip_xml_tags`    | `bool`    | `true`  | Drop arbitrary `<foo/>` / `<bar>…</bar>` (broader than `strip_html`). |
| `markdown.strip_frontmatter` | `bool`    | `true`  | Strip the leading `--- … ---` YAML frontmatter block. |
| `markdown.strip_ai_signatures` | `bool`  | `true`  | Strip "Generated with Claude/GPT/…", "Co-Authored-By: …", emoji sign-offs. |
| `markdown.strip_mentions`    | `bool`    | `true`  | Strip `@user` and `#channel` mentions. |
| `markdown.strip_unicode_md`  | `bool`    | `true`  | Strip invisible Unicode code points (same code path as text Layer A). |
| `markdown.strip_trailing_ws` | `bool`    | `true`  | Strip trailing whitespace per line. |
| `markdown.apply_unicode_layer_a` | `bool`| `true`  | Run the Layer A Unicode hygiene pass over prose (set to `false` to skip normalisation entirely). |
| `markdown.preserve_code_blocks` | `bool` | `true`  | **Force** preserve fenced code blocks regardless of other settings. The cleaner always preserves fences unless `strip_code_fences = true`; this key is a legacy CLI knob kept for backward compatibility. |

### Example

```yaml
markdown:
  strip_headings: false         # keep the document structure
  strip_code_fences: false
  strip_inline_code: false
  strip_links: false
  strip_images: true            # but kill embedded images
  strip_bold_italic: false
  strip_blockquotes: false
  strip_hr: true
  strip_html: true
  strip_comments: true
  strip_task_lists: false
  strip_table_syntax: false
  normalize_lists: true
  unwrap_empty_lists: true
  strip_xml_tags: true
  strip_frontmatter: true
  strip_ai_signatures: true
  strip_mentions: true
  strip_unicode_md: true
  strip_trailing_ws: true
  apply_unicode_layer_a: true
  preserve_code_blocks: true
```

Use the `--strip-all` CLI flag to enable *every* transform at once
(effectively a "kill everything but the prose" mode). The HTTP
endpoint `POST /clean/markdown` honours the same `strip_all: true`
flag via the request body.

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

## `server`

Settings that only apply when the HTTP API is up (`serve` command).
Other commands (CLI cleaning, batch jobs) ignore this section entirely.

| Key                            | Type   | Default | Description |
|--------------------------------|--------|---------|-------------|
| `server.rate_limit.permit_limit` | `int` | `100`   | Maximum requests allowed per `window_seconds` per remote IP. Lower for stricter throttling. |
| `server.rate_limit.window_seconds` | `int` | `60`  | Length of the fixed-window counter, in seconds. Shorter windows give more frequent bursts. |
| `server.rate_limit.queue_limit` | `int`  | `0`    | Maximum requests to queue when the limit is hit. `0` = reject immediately with HTTP 429. |
| `server.max_upload_mb`         | `int`  | `100`   | Maximum request body size, in MB, for multipart uploads (`/clean/file`, `/clean/image`, `/inspect/file`, `/detect/image`). Oversized uploads are rejected with HTTP 413 before the body is streamed to disk. `0` disables the limit (not recommended for public deployments). |

### Example

```yaml
server:
  rate_limit:
    permit_limit: 200     # allow 200 req / minute
    window_seconds: 60
    queue_limit: 0        # reject immediately on overflow
  max_upload_mb: 50       # reject uploads larger than 50 MB
```

### CLI overrides

The `serve` command accepts flags that take precedence over `config.yaml`:

| Flag                          | Type | Overrides                                  |
|-------------------------------|------|--------------------------------------------|
| `--rate-limit <REQUESTS>`     | `int` | `server.rate_limit.permit_limit`           |
| `--rate-window <SECONDS>`     | `int` | `server.rate_limit.window_seconds`         |
| `--max-upload-mb <MEGABYTES>` | `int` | `server.max_upload_mb`                     |

Resolution order (first match wins):

1. CLI flag (`--rate-limit` / `--rate-window` / `--max-upload-mb`)
2. `server.*` from `config.yaml`
3. Built-in defaults (100 / 60 / 0 / 100 MB)

`--rate-limit` and `--rate-window` must be `> 0`; `--max-upload-mb` must be
`>= 0` (0 disables the limit). The server exits with status `1` if any
value is invalid. The active values are printed at start-up so operators
can confirm the source (`config.yaml` vs. CLI override) at a glance.

---

## `mcp`

Settings for the [`serve-mcp`](../README.md#commands) command
(WR-S11). The MCP server exposes the full pipeline as eight
Model Context Protocol tools so MCP-compatible agents
(Claude Code, OpenCode, MiniMax Code, Cursor, Continue, …) can
call `clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
`detect_text`, `detect_markdown`, `inspect_file`, and
`detect_watermark` directly.

| Key | Type | Default | Description |
|---|---|---|---|
| `transport` | `string` | `"stdio"` | Transport to bind. `"stdio"` (default) — local stdio pipe for Claude Code / OpenCode / MiniMax Code / Cursor / Continue. `"http"` — Streamable HTTP transport for remote agents and Docker. Unknown values fail at start-up with a clear error. |
| `host` | `string` | `"0.0.0.0"` | Interface to bind the HTTP transport to. Ignored for stdio. |
| `port` | `int` | `5090` | TCP port for the HTTP transport. Ignored for stdio. Distinct from `server.*`'s 5080 so `serve` and `serve-mcp` can run side by side. |
| `api_key` | `string` or `null` | `null` | When set, every HTTP request must carry the matching `X-API-Key` header. Same auth pattern as the regular `serve` command. Ignored for stdio (the pipe is the auth boundary). |
| `rate_limit` | object or `null` | `null` (→ `server.rate_limit`) | Per-IP rate-limit policy for the HTTP transport. When `null`, inherits the `server.rate_limit` block. Ignored for stdio. |
| `rate_limit.permit_limit` | `int` | `100` | Requests allowed per window, per IP. |
| `rate_limit.window_seconds` | `int` | `60` | Window length, in seconds. |
| `rate_limit.queue_limit` | `int` | `0` | 0 = reject immediately on overflow (HTTP 429 with no queueing). |

### Example

```yaml
mcp:
  transport: "http"           # stdio for local agents; http for remote / Docker
  host: "0.0.0.0"
  port: 5090
  api_key: null               # leave null for localhost dev; set a real key for public
  rate_limit:
    permit_limit: 100
    window_seconds: 60
    queue_limit: 0
```

### CLI overrides

| Flag | Overrides |
|---|---|
| `--transport <stdio\|http>` | `mcp.transport` |
| `-H\|--host <HOST>` | `mcp.host` (HTTP only) |
| `-p\|--port <PORT>` | `mcp.port` (HTTP only) |
| `--api-key <KEY>` | `mcp.api_key` (HTTP only) |
| `--rate-limit <REQUESTS>` | `mcp.rate_limit.permit_limit` (HTTP only) |
| `--rate-window <SECONDS>` | `mcp.rate_limit.window_seconds` (HTTP only) |

Resolution order (first match wins):

1. CLI flag
2. `mcp.*` from `config.yaml`
3. `server.rate_limit` (for the rate-limit knobs only)
4. Built-in defaults

`--rate-limit` and `--rate-window` must be `> 0`; `serve-mcp` exits
with status `1` if any value is invalid. The active values are
printed at start-up so operators can confirm the source
(`config.yaml` vs. CLI override) at a glance.

### stdout / stderr contract (stdio transport)

`serve-mcp --transport stdio` uses stdout exclusively for the
JSON-RPC protocol stream. **All** logging is routed to stderr via
`LogToStandardErrorThreshold = LogLevel.Trace` so the agent's
JSON parser never sees a stray log line. This matches the
guidance from the [MCP stdio spec](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio).

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
  strip_bold_italic: false
  strip_blockquotes: false
  strip_hr: true
  strip_html: true
  strip_comments: true
  strip_task_lists: false
  strip_table_syntax: false
  normalize_lists: true
  unwrap_empty_lists: true
  strip_xml_tags: true
  strip_frontmatter: true
  strip_ai_signatures: true
  strip_mentions: true
  strip_unicode_md: true
  strip_trailing_ws: true
  apply_unicode_layer_a: true
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

server:
  rate_limit:
    permit_limit: 100
    window_seconds: 60
    queue_limit: 0
  max_upload_mb: 100

mcp:
  transport: "stdio"
  host: "0.0.0.0"
  port: 5090
  api_key: null
  rate_limit:
    permit_limit: 100
    window_seconds: 60
    queue_limit: 0
```

---

## Validation

`ConfigLoader` is **lenient** today — unknown keys are ignored, and
invalid values fall back to defaults silently. Strict validation (fail
fast on unknown keys) is on the
[BACKLOG.md → P2](./BACKLOG.md#p2--platform--ux-v1x) list. Until then,
use `--verbose` to see which keys were applied.
