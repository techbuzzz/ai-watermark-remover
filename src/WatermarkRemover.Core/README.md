# WatermarkRemover.Core

Core models, interfaces, and configuration shared by the
[WatermarkRemover](https://github.com/techbuzzz/ai-watermark-remover) family of
libraries. This package is a **leaf** project — it references no other
WatermarkRemover package and is the dependency root for
`WatermarkRemover.Text`, `WatermarkRemover.Metadata`, and
`WatermarkRemover.Image`.

## What's in this package

- `IFileCleanerRouter` / `FileCleanerRouter` — extension-point contracts for
  metadata cleaners and the default router implementation.
- `IImageCleaningPipeline` / `ITextCleaningPipeline` / `IMarkdownCleaner` —
  interfaces the text, metadata, and image packages implement.
- `AppConfig`, `TextCleanOptions`, `MarkdownCleanOptions`,
  `MaxUploadMBConfig`, `RateLimitConfig` — strongly-typed configuration
  models loaded from `config.yaml`.
- `WatermarkMatch`, `DetectedRegion`, `MetadataEntry`, `ErrorResult` — the
  POCO shapes the CLI / HTTP / MCP layers exchange with the rest of the
  pipeline.

## Install

```powershell
dotnet add package WatermarkRemover.Core
```

## Use

```csharp
using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core;

var services = new ServiceCollection();
services.AddWatermarkRemoverCore(AppConfig.Default);
var provider = services.BuildServiceProvider();

IFileCleanerRouter router = provider.GetRequiredService<IFileCleanerRouter>();
foreach (string file in Directory.EnumerateFiles("./input"))
{
    if (router.IsSupported(Path.GetExtension(file)))
    {
        Console.WriteLine($"{file}: {router.Inspect(file).Entries.Count} metadata entries");
    }
}
```

## Project conventions

- Nullable reference types are **enabled** — every public surface is
  annotated.
- All public types are designed to be `Microsoft.Extensions.DependencyInjection`
  friendly (constructor injection, no static state, no service locators).

## License

[MIT](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE) — see the
root repository for full attribution and contributor list.
