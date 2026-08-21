# 🏛️ Architecture

A high-level map of the codebase so new contributors can find their
bearings in under five minutes.

## Bird's-eye view

```
                       ┌──────────────────────────┐
                       │  Spectre.Console CLI     │ ← WatermarkRemover.CLI
                       │  ASP.NET Core HTTP API   │
                       └────────────┬─────────────┘
                                    │ wires via DI
        ┌───────────────────────────┼───────────────────────────┐
        │                           │                           │
        ▼                           ▼                           ▼
┌────────────────┐         ┌────────────────┐         ┌────────────────┐
│ WatermarkRe-   │         │ WatermarkRe-   │         │ WatermarkRe-   │
│ mover.Text     │         │ mover.Metadata │         │ mover.Image    │
│ (Layer A/B/C + │         │ (byte-level    │         │ (mask + LaMa   │
│  Markdown)     │         │  cleaners)     │         │  inpainting)   │
└────────┬───────┘         └────────┬───────┘         └────────┬───────┘
         │                          │                           │
         └──────────────────────────┼───────────────────────────┘
                                    ▼
                       ┌──────────────────────────┐
                       │  WatermarkRemover.Core   │ ← models, interfaces, config
                       │  (no behaviour, DI only) │
                       └──────────────────────────┘
```

**Key invariants:**

- `Core` is a leaf project — every other project may reference it; it
  references nobody.
- `Text`, `Metadata`, `Image` are siblings — none of them references
  the others. Cross-cutting behaviour goes through `Core` interfaces.
- `CLI` is the composition root. It owns DI registration and is the
  only place that knows about all four siblings.

## Data flow

### Text cleaning (`clean-text`)

```
input text
  │
  ▼
┌─────────────────────────────┐
│ Layer A: UnicodeHygiene     │  strips invisible code points
│ Cleaner                     │  applies NFKC + homoglyph folding
└─────────────┬───────────────┘
              ▼
┌─────────────────────────────┐
│ Layer B: Statistical        │  EN + RU synonym dictionary
│ WatermarkRewriter           │  optional LLM back-translation
└─────────────┬───────────────┘  (Ollama-compatible)
              ▼
┌─────────────────────────────┐
│ Layer C: Vendor Detectors   │  Claude / Gemini / OpenAI
│ (ClaudeDetector,            │  heuristic patterns
│  GeminiDetector, …)         │
└─────────────┬───────────────┘
              ▼
       TextCleanResult
```

Each layer is independent and configurable. The pipeline is a
[Chain of Responsibility](https://en.wikipedia.org/wiki/Chain-of-responsibility_pattern)
that short-circuits per layer based on the `TextCleanOptions`.

### Markdown cleaning (`clean-markdown`)

```
markdown
  │
  ▼
┌──────────────────────────────┐
│ CodeBlockParser              │  identifies fenced blocks → preserve
└─────────────┬────────────────┘
              ▼
┌──────────────────────────────┐
│ MarkdownCleaner              │  20+ toggleable transforms
│ (apply options.Trans‑        │  (headings, links, images, frontmatter,
│  forms)                      │   AI signatures, …)
└─────────────┬────────────────┘
              ▼
      MarkdownCleanResult
```

Code blocks are parsed out **once** at the start so the transforms can
operate on the prose without mangling `<pre>` content.

### Metadata cleaning (`clean-file`)

```
input file → FileCleanerRouter.IsSupported(extension)?
                              │
        ┌────────────┬────────┼────────┬───────────┐
        ▼            ▼        ▼        ▼           ▼
    JpegCleaner  PngCleaner  PdfCleaner  DocxCleaner  HtmlCleaner
        │            │        │        │           │
        └────────────┴────────┼────────┴───────────┘
                             ▼
                       cleaned file
```

The router is a single `switch` on file extension. To add a new format,
implement `IFileMetadataCleaner` and register the extension in
[`FileCleanerRouter`](../src/WatermarkRemover.Metadata/FileCleanerRouter.cs).

### Image cleaning (`clean-image`)

```
input image
  │
  ▼
┌──────────────────────────────┐
│ MaskGenerator                │  alpha + colour-frequency
│ (auto-detect or --mask)      │  connected-component extraction
└─────────────┬────────────────┘
              ▼
┌──────────────────────────────┐
│ ImageCleaningPipeline        │  resize → infer (ONNX) → blend
│  ↳ LamaInpaintingService     │
│  ↳ IInpaintRunner (DI)       │  FakeInpaintRunner in tests
└─────────────┬────────────────┘
              ▼
       cleaned image
```

`IInpaintRunner` is the seam that lets tests substitute a deterministic
fake for the LaMa ONNX runtime. The CLI wires the real
`LamaInpaintingService`; the test project wires the fake.

## Configuration model

[`AppConfig`](../src/WatermarkRemover.Core/Configuration/AppConfig.cs)
is loaded by [`ConfigLoader`](../src/WatermarkRemover.CLI/Infrastructure/ConfigLoader.cs)
from the first matching path:

1. `--config <path>` (CLI flag)
2. `./config.yaml` (CWD)
3. `config.yaml` next to the apphost
4. Built-in defaults

`ConfigLoader` ignores unknown keys so adding new sections is a
non-breaking change. See [CONFIGURATION.md](./CONFIGURATION.md) for
every supported key.

## Dependency injection

Each module exposes a `DependencyInjection.cs` with a single extension
method (`AddWatermarkRemoverCore` / `AddWatermarkRemoverText` / …).
The composition root wires them in
[`Program.cs`](../src/WatermarkRemover.CLI/Program.cs).

```csharp
ServiceCollection services = new();
services.AddWatermarkRemoverCore(config);
services.AddWatermarkRemoverText();
services.AddWatermarkRemoverMetadata();
services.AddWatermarkRemoverImage();
```

The HTTP API in `serve` reuses the same singleton services — it does
**not** rebuild the pipeline per request.

## Extension points

| Want to …                                         | Touch…                                              |
|---------------------------------------------------|-----------------------------------------------------|
| Add a new vendor detector                         | `src/WatermarkRemover.Text/Vendors/<Name>WatermarkDetector.cs` + DI registration + tests |
| Add a new metadata format                         | `src/WatermarkRemover.Metadata/<Format>MetadataCleaner.cs` + router entry + tests |
| Add a new inpainting backend                      | Implement `IInpaintRunner`, register in `WatermarkRemover.Image.DependencyInjection` |
| Add a new CLI command                             | `src/WatermarkRemover.CLI/Commands/<Name>Command.cs` + `cfg.AddCommand<…>(…)` in `Program.cs` |
| Add a new HTTP endpoint                           | `src/WatermarkRemover.CLI/Commands/ServeCommand.cs` → `MapEndpoints(...)` |
| Change the default config                         | `src/config.yaml` (canonical example) + defaults in `AppConfig` |
| Add a new dependency                              | Open an issue first — see [CONTRIBUTING.md](../CONTRIBUTING.md#coding-conventions) |

## Test strategy

| Layer        | Project                              | What it covers                                   |
|--------------|--------------------------------------|--------------------------------------------------|
| Unit         | `WatermarkRemover.Text.Tests`        | Each cleaner, vendor detector, MarkdownCleaner, CodeBlockParser |
| Unit         | `WatermarkRemover.Metadata.Tests`    | Each format cleaner (round-trip), router routing |
| Unit         | `WatermarkRemover.Image.Tests`       | MaskGenerator, ImageCleaningPipeline (with `FakeInpaintRunner`), LamaInpaintingService |
| Integration  | *(planned)*                          | End-to-end CLI / HTTP via `WebApplicationFactory` — see [BACKLOG.md → P3](../BACKLOG.md#p3--quality--reliability-ongoing) |
| Property     | *(planned)*                          | FsCheck invariants for `UnicodeHygieneCleaner` — see [BACKLOG.md → P3](../BACKLOG.md#p3--quality--reliability-ongoing) |
| Benchmarks   | *(planned)*                          | BenchmarkDotNet throughput / regression — see [BACKLOG.md → P3](../BACKLOG.md#p3--quality--reliability-ongoing) |

Image tests deliberately do **not** require the LaMa model: they inject
a `FakeInpaintRunner` that returns the input image with a synthetic
inpainted region, so `dotnet test` works on any machine without
network access.

## Build pipeline

```
┌──────────────────┐
│  PR / push       │
└────────┬─────────┘
         ▼
┌──────────────────────────────────────────────┐
│ .github/workflows/build-and-test.yml         │  Windows + Ubuntu matrix
│ restore → build → test (TRX + cobertura)     │  uploads test + coverage artifacts
└──────────────────────────────────────────────┘

┌──────────────────┐
│  git tag vX.Y.Z  │
└────────┬─────────┘
         ▼
┌──────────────────────────────────────────────┐
│ .github/workflows/release.yml                │  4-RID matrix → GitHub Release
│ publish (self-contained, single-file)        │  zips → attach to release
│ → zip → upload → gh release create           │
└──────────────────────────────────────────────┘
```

See [ci-release.md](./ci-release.md) for the longer write-up.
