# ⚖️ Comparison with other tools

> How does WatermarkRemover stack up against the existing ecosystem?
> This page is maintained as the landscape changes — please open a PR
> if a tool's status is wrong.

## At a glance

|                                  | **WatermarkRemover** | [exiftool] | [mat2] | [exiv2] | [SD-WatermarkRemover] | [MarkPrompt] | [AI-Watermark-Remover] |
|----------------------------------|----------------------|------------|--------|---------|-----------------------|--------------|------------------------|
| **Language / stack**             | .NET 10              | Perl       | Python | C++     | Python (Stable Diffusion) | TypeScript | Python (community)    |
| **Self-contained binary**        | ✅ Single file        | ❌ Perl runtime | ❌ Python runtime | ❌ native | ❌ Python runtime | ❌ Node runtime | ❌ Python runtime |
| **Text Unicode hygiene**         | ✅                    | ❌          | ❌      | ❌       | ❌                     | ❌            | ✅                      |
| **Statistical token rewrite**    | ✅ EN + RU            | ❌          | ❌      | ❌       | ❌                     | ❌            | ❌                      |
| **Vendor AI-text detectors**     | ✅ Claude/Gemini/OAI  | ❌          | ❌      | ❌       | ❌                     | ❌            | ⚠️ OpenAI only         |
| **Markdown cleaning**            | ✅ 20+ transforms     | ❌          | ❌      | ❌       | ❌                     | ❌            | ❌                      |
| **JPEG metadata**                | ✅ Byte-level         | ✅ Best-in-class | ✅ | ✅ | ❌ | ❌ | ❌ |
| **PNG metadata**                 | ✅ Byte-level         | ✅          | ✅      | ✅       | ❌                     | ❌            | ❌                      |
| **PDF metadata**                 | ✅ PdfPig rebuild     | ⚠️ Partial  | ✅      | ⚠️ Partial | ❌                  | ❌            | ❌                      |
| **DOCX metadata**                | ✅ OpenXML            | ⚠️ Partial  | ✅      | ❌       | ❌                     | ❌            | ❌                      |
| **HTML metadata**                | ✅                    | ❌          | ⚠️      | ❌       | ❌                     | ❌            | ❌                      |
| **Visual watermark inpainting**  | ✅ LaMa ONNX          | ❌          | ❌      | ❌       | ✅ (SD-specific)       | ❌            | ❌                      |
| **HTTP API**                     | ✅ Built-in           | ❌          | ❌      | ❌       | ❌                     | ✅ SaaS        | ❌                      |
| **Docker image**                 | ✅ Non-root           | ✅          | ✅      | ✅       | ❌                     | ❌            | ❌                      |
| **First-class Russian support**  | ✅ Synonym dict       | n/a        | n/a    | n/a     | n/a                    | n/a          | n/a                     |
| **License**                      | MIT                  | Artistic 1.0 | LGPL-2.1 | GPL-2.0 | MIT | Proprietary | MIT |

✅ = supported, ⚠️ = partial / lossy, ❌ = not supported, n/a = not applicable.

[exiftool]: https://exiftool.org/
[mat2]: https://0xacab.org/jvoisin/mat2
[exiv2]: https://exiv2.org/
[SD-WatermarkRemover]: https://github.com/boombuler/SD-WatermarkRemover
[MarkPrompt]: https://markprompt.com/
[AI-Watermark-Remover]: https://github.com/guoyahao/AI-Watermark-Remover

## What WatermarkRemover does *better*

- **All-in-one** — no need to chain `exiftool | jq | sed` to clean a
  document end-to-end. One binary cleans text, markdown, metadata, and
  visual watermarks with one config file.
- **Russian language support** — homoglyph-safe Unicode normalisation
  and a built-in Russian synonym dictionary for Layer B. No other tool
  in this space is Russian-aware out of the box.
- **HTTP API with auth + rate limit** — usable as a microservice
  without bolting on nginx + lua.
- **Single-file distribution** — release artifacts are self-contained
  single-file binaries for 4 RIDs. No runtime install for end users.
- **Honest scope** — vendor detectors are documented as heuristic
  (the underlying schemes are key-based and not publicly verifiable)
  rather than overpromising.

## Where other tools are stronger

- **exiftool** — far more metadata formats, more robust on exotic
  manufacturer makernotes, faster on huge image batches. If you only
  need JPEG/PNG metadata stripping and already have Perl, keep using it.
- **mat2** — excellent security audit (fuzz-tested, formally verified
  metadata removal for threat models) and broader image format support.
  Use it if your threat model is "lawyer-grade metadata erasure".
- **exiv2** — battle-tested native library, lots of language bindings
  (Python, Ruby, Node). Pick it when you need to embed in an existing
  C++/Python app.
- **SD-WatermarkRemover** — purpose-built for the Stable Diffusion
  inpainting workflow (uses the SD pipeline, not LaMa). Better if you
  already run SD locally.
- **MarkPrompt / similar SaaS** — appropriate when you don't want to
  host anything and you trust a third party with your documents.

## When to pick WatermarkRemover

- You want a **single binary** that runs on every developer machine
  with no setup.
- You need to clean **text or markdown** (not just metadata).
- Your content includes **Russian / Cyrillic** and you need it to
  survive cleaning intact.
- You want to **embed the cleaner** in a CI pipeline, sidecar, or
  HTTP service.
- You prefer **MIT-licensed** code with no Perl / Python / native
  dependency.

## When **not** to pick WatermarkRemover

- You only need exiftool-class metadata stripping on a single format —
  use the focused tool.
- You need formally-verified metadata erasure for a legal threat model —
  use mat2.
- You need a managed SaaS and don't want to host anything — use a
  third-party API.
- You're removing watermarks from third-party content in violation of
  the relevant platform's terms — see
  [SECURITY.md → Responsible use](../SECURITY.md#responsible-use).
