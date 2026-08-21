# ❓ Frequently asked questions

> Can't find your question here? Open a
> [GitHub Discussion](https://github.com/techbuzzz/ai-watermark-remover/discussions)
> or file a [bug report](https://github.com/techbuzzz/ai-watermark-remover/issues/new?template=bug-report.yml).

## General

### What is WatermarkRemover?

A cross-platform .NET 10 toolkit (CLI + HTTP API) that strips AI-provenance
watermarks and metadata from **text**, **markdown**, **files** (JPEG/PNG/PDF/DOCX/HTML),
and **images** (visual watermarks via LaMa inpainting).

### Why .NET and not Python / Node / Rust?

- **One binary, no runtime install** for end users — the release pipeline
  ships self-contained single-file executables for 4 RIDs.
- **Cross-platform** (Windows / Linux / macOS, x64 + ARM64) with first-class
  CI matrix coverage.
- **Performance** — `System.IO.Pipelines` + the BCL image/IO stack rival
  C++/Rust for byte-level cleaners while keeping the development velocity of a
  managed language.
- **Ecosystem** — `PdfPig` for PDF, ONNX Runtime for ML, Spectre.Console for
  the CLI, ASP.NET Core for the HTTP API: battle-tested libraries, one stack.

That said, see [BACKLOG.md → P4 — Integrations](./BACKLOG.md#p4--ecosystem--reach-post-10):
Python (`pyo3`) and Node.js (`napi-rs`) bindings are on the roadmap.

### Is it legal? Is it ethical?

The tool has many legitimate uses (cleaning your own output, corpus
normalisation, security research, forensic analysis). Removing watermarks from
third-party content to evade attribution may violate the relevant platform's
terms of service or applicable law — see
[SECURITY.md → Responsible use](../SECURITY.md#responsible-use) for the full
policy.

### Is this a fork of an existing project?

No — it's a from-scratch implementation. The architecture is informed by
prior work in the mat2 / exiftool space, but the code is original.

---

## Installation & setup

### Which .NET version do I need?

.NET 10.0 SDK or runtime (specifically **10.0.400** as pinned in
[`global.json`](../global.json)). If you only run the prebuilt binary, you
need the .NET 10 **runtime** (or use the self-contained single-file
download, which bundles it).

### I installed .NET 10 but `dotnet build` says "SDK not found".

`global.json` pins the **exact** patch (10.0.400) with
`rollForward: latestPatch`. Either install 10.0.400, or temporarily move
`global.json` aside. Don't change `rollForward` to `latestMajor` — it
will silently let 11.x SDKs build against 10.0 target frameworks and
produce broken binaries.

### Where is the LaMa model? Why isn't it bundled?

The model is **not** redistributed with the binary because its licence
is unclear for redistribution. Run `watermarkremover download-model` to
fetch and extract it (~200 MB). The downloader falls back gracefully
when no `.onnx` is present in the upstream archive.

### Can I run this on an air-gapped machine?

Yes. The CLI has **no** hard network dependencies at runtime. For
image inpainting, pre-download the model on a connected machine
(`download-model` writes to `./models/`) and copy that directory to
the air-gapped host.

### Docker image: does it work on Apple silicon / Raspberry Pi?

The `linux-arm64` release artifact is published. The Dockerfile currently
targets `linux-musl-x64` because the official .NET alpine base images only
ship x64. For ARM64, build on an ARM64 host (`docker buildx build
--platform linux/arm64`).

---

## Text cleaning

### Will WatermarkRemover damage my text?

Layer A is **idempotent** on any text that doesn't contain invisible
code points. Layer B (statistical) replaces tokens with synonyms — by
definition it rewrites, so it can change meaning. **Disable Layer B if
you need byte-perfect preservation** (`--no-statistical` / config).
Layer C only removes vendor-specific markers; it never alters human-readable
text.

### How accurate are the vendor detectors (Claude, Gemini, OpenAI)?

They are **best-effort heuristics**. The underlying vendor schemes are
key-based and not publicly verifiable, so we can't claim 100 % accuracy.
See the `IAiTextWatermarkDetector` interface for the contract — every
implementation documents its limitations.

### Does Layer A break Cyrillic / Russian text?

No. The homoglyph normaliser is gated on the *neighbouring* characters
— it only folds look-alike characters when they sit between Latin
letters. Genuine Russian words like «Привет мир» are not touched. This
invariant is locked in by `UnicodeHygieneCleanerTests`.

### How do I keep my markdown structure?

By default the markdown cleaner only strips AI-specific artifacts
(frontmatter, "as an AI" lines, trailing whitespace, emoji sign-offs).
Pass `--strip-all` to also strip headings, images, links, and HTML
blocks. Code fences are preserved either way.

---

## Metadata cleaning

### Is the cleaning pixel-preserving?

**Yes** for JPEG, PNG, DOCX, HTML. The cleaner parses the format, removes
the metadata segments / chunks / properties, and rewrites the container
without re-encoding pixel data. For PDF, the document body is rebuilt
through `PdfPig` to strip document-info / XMP cleanly; this preserves
text but reflows the page tree, so the byte-for-byte file will differ.

### Does it strip C2PA manifests?

Yes — by default (`strip_c2pa: true` in `config.yaml`). C2PA is just
another metadata stream as far as the JPEG / PNG / PDF cleaners are
concerned. To replace the manifest with a clean stub instead of removing
it, see [BACKLOG.md → C2PA manifest injection](./BACKLOG.md#metadata-cleaner-enhancements).

### Will exiftool do the same thing faster?

For JPEG / PNG / TIFF / WebP, **yes, exiftool is faster and more
thorough**. Where WatermarkRemover earns its keep is the *combination*
of metadata stripping + text/markdown cleaning + image inpainting in a
single binary, with the same configuration file. See
[COMPARISON.md](./COMPARISON.md).

---

## Image cleaning

### Do I need the LaMa model to use WatermarkRemover?

**No.** Without the model, image commands run the full mask-generation
pipeline (alpha + colour-frequency heuristics) and copy the source
image unchanged. The CLI reports "model missing" so it's obvious.

### Will it work on transparent PNGs?

Yes — the mask generator uses alpha-channel analysis as its primary
heuristic. If your watermark is fully opaque on a fully opaque
background, the colour-frequency heuristic kicks in.

### Can I supply my own mask?

Yes — pass `--mask <path-to-png>` to `clean-image`. White pixels = fill
this region. The pipeline resizes the mask to match the input image.

### Does it work on huge images (>4K)?

The current release processes images at their native resolution. For
very large inputs (>8K) the LaMa model may exceed available VRAM/RAM
in the ONNX CPU execution provider. Workarounds:

- Use `--mask` to limit the work to a specific region.
- Pre-downscale with ImageMagick or similar.
- GPU inference is on the [BACKLOG.md → P1](./BACKLOG.md#image-pipeline-enhancements) list.

### How long does inpainting take?

Roughly 1–3 seconds per megapixel on a modern CPU. The pipeline
exposes a `--mask` shortcut that lets you skip auto-detection for
repeatable work.

---

## HTTP API

### Is the server production-ready?

It's a Minimal API on top of Kestrel — the same stack that ships in
ASP.NET Core. The defaults are sensible for a self-hosted homelab /
side-project, but for a public-facing deployment you should:

- Front it with a reverse proxy that terminates TLS (Caddy / nginx / Traefik).
- Set `--api-key`.
- Run it as a non-root user (the Docker image already does this).
- Restrict source IPs at the firewall.
- Front it with a managed gateway for rate limiting + DDoS protection.

### Where's the OpenAPI / Swagger UI?

On the [BACKLOG.md → P2](./BACKLOG.md#http-api-enhancements) list. In
the meantime, the API is small enough to fit on one screen — see
[README.md → HTTP API](../README.md#-http-api-serve).

### Why is the rate limit 100 req/min/IP hardcoded?

It can be configured per-deployment via a reverse proxy. Surfacing it
in `config.yaml` is on the
[BACKLOG.md → P2](./BACKLOG.md#http-api-enhancements) list.

---

## Contributing

### I'm new to .NET — can I still contribute?

Yes! The codebase is intentionally C# 12 / .NET 10 idiomatic. Start
with issues tagged
[`good first issue`](https://github.com/techbuzzz/ai-watermark-remover/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
and read [CONTRIBUTING.md](../CONTRIBUTING.md).

### How do I add a new vendor detector / metadata cleaner?

Step-by-step recipes in
[CONTRIBUTING.md → Adding a new vendor detector](../CONTRIBUTING.md#-adding-a-new-vendor-detector)
and
[CONTRIBUTING.md → Adding a new metadata cleaner](../CONTRIBUTING.md#-adding-a-new-metadata-cleaner).

### How do I run a single test class?

```bash
dotnet test --filter "FullyQualifiedName~UnicodeHygieneCleanerTests"
```

### Where do bugs / RFEs go?

- 🐛 **Bugs** — use the
  [bug report template](https://github.com/techbuzzz/ai-watermark-remover/issues/new?template=bug-report.yml).
- ✨ **Feature requests** — use the
  [feature request template](https://github.com/techbuzzz/ai-watermark-remover/issues/new?template=feature-request.yml).
- 💡 **Open-ended questions / ideas** —
  [Discussions](https://github.com/techbuzzz/ai-watermark-remover/discussions).
- 🔐 **Security** — email per [SECURITY.md](../SECURITY.md) (do **not** file
  a public issue).
