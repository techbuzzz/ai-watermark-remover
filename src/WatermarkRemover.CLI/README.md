# WatermarkRemover

Cross-platform .NET 10 CLI / HTTP API / MCP server that strips
AI-provenance watermarks from **text**, **markdown**, **files**
(JPEG / PNG / PDF / DOCX / HTML / WebP metadata), and **images**
(LaMa inpainting). One binary, first-class Russian support,
plug-in vendor detectors (Claude / Gemini / OpenAI).

This package is the `dotnet tool` entry point — see the
[main repository](https://github.com/techbuzzz/ai-watermark-remover)
for the full architecture, all 8 MCP tool schemas, configuration
reference, Docker recipes, and agent-skill catalog.

## Install

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download)
or .NET 10 Runtime with ASP.NET Core.

```bash
dotnet tool install -g watermarkremover
watermarkremover --version
```

Update later:

```bash
dotnet tool update -g watermarkremover
```

Uninstall:

```bash
dotnet tool uninstall -g watermarkremover
```

If the install path isn't on your `$PATH` after a global install, see
[the .NET docs on the default install location](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools#install-a-global-tool).
On most shells it is `$HOME/.dotnet/tools` on Linux/macOS and
`%USERPROFILE%\.dotnet\tools` on Windows.

## Common commands

```bash
# Strip invisible characters (ZWSP / ZWJ / LRM / homoglyphs) from text
watermarkremover clean-text "Hello‍‎world"   # → Helloworld

# Strip AI metadata from a folder of photos (recursive)
watermarkremover clean-file ./photos --recursive

# Remove a visual watermark from an image (auto-detected mask + LaMa inpainting)
watermarkremover download-model
watermarkremover clean-image photo.png -o clean.png

# Spin up the HTTP API + (optional) web UI on :5080
watermarkremover serve --port 5080 --api-key s3cret
# → open http://localhost:5080/

# Expose the pipeline as MCP tools for Claude Code, OpenCode, MiniMax Code,
# Cursor, Continue, etc. (stdio default; --transport http for Streamable HTTP)
watermarkremover serve-mcp
```

## Other install paths

The `dotnet tool` install is the smallest, most idiomatic .NET
install — one NuGet package, one command. For alternatives:

- **Pre-built single-file binary** (no .NET runtime required,
  web UI embedded) — see the
  [GitHub Releases](https://github.com/techbuzzz/ai-watermark-remover/releases/latest)
- **Docker** (multi-stage, UI included) — `docker run --rm -p 5080:5080 watermarkremover`
- **`@watermarkremover/mcp`** npm wrapper for Cursor / Continue
  (downloads the platform-appropriate release binary on `npm install`)

## Requirements

- .NET 10 Runtime (or SDK) with ASP.NET Core
- ~70 MB disk for the tool install + its restore graph
- (Optional) a LaMa ONNX model file for `clean-image` /
  `clean-image-batch` — `watermarkremover download-model` fetches it
  on first use

## Project conventions

- Nullable reference types are **enabled** — every public surface is
  annotated.
- All logging routes through the standard `Microsoft.Extensions.Logging`
  pipeline (Serilog is the wired sink).
- Configuration is loaded from `config.yaml` next to the tool's
  working directory (or the path passed via `--config`).
- The exit-code contract is documented in
  [`docs/CONFIGURATION.md`](https://github.com/techbuzzz/ai-watermark-remover/blob/main/docs/CONFIGURATION.md):
  `0` success, `1` input error, `2` detections found, `3` unsupported
  format, `4` model missing.

## License

[MIT](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE) —
see the root repository for full attribution and contributor list.
