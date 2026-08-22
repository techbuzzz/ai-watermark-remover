# WatermarkRemover.Image

Inpainting pipeline for visual watermark removal. Auto-detects the
watermark mask from alpha + colour frequency, then runs the LaMa ONNX
model (`IInpaintRunner`) to inpaint the masked region. The runner is
the only swappable piece: tests inject a `FakeInpaintRunner` so the
suite runs without the ONNX model.

## What's in this package

- `IImageCleaningPipeline` / `ImageCleaningPipeline` — resize → mask →
  infer → blend.
- `MaskGenerator` — alpha + colour-frequency connected-component
  extraction; auto-mask or `--mask-rect` override.
- `LamaInpaintingService` — the default `IInpaintRunner`, ONNX Runtime
  + the bundled LaMa model.
- `ImageCleanOptions`, `MaskRect`, `DetectedRegion` — public options and
  DTOs.

## Install

```powershell
dotnet add package WatermarkRemover.Image
```

You will also need the LaMa model file (the `IInpaintRunner` contract
takes any 2048×2048 ONNX mask-in / RGB-in / RGB-out LaMa-style
model). The CLI ships with the model; library consumers can download
it from the [releases page](https://github.com/techbuzzz/ai-watermark-remover/releases).

## Use

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WatermarkRemover.Core;
using WatermarkRemover.Image;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddWatermarkRemoverCore(AppConfig.Default);
services.AddWatermarkRemoverImage();
var provider = services.BuildServiceProvider();

IImageCleaningPipeline pipeline = provider.GetRequiredService<IImageCleaningPipeline>();
using var input = File.OpenRead("logo.png");
using var output = new MemoryStream();
ImageCleanResult result = await pipeline.CleanAsync(input, output, new ImageCleanOptions());
Console.WriteLine($"Cleaned in {result.Elapsed}ms — {result.DetectedRegions.Count} region(s)");
```

## Project conventions

- `IInpaintRunner` is the **only** seam for swapping the inference
  backend. Production wires `LamaInpaintingService`; tests wire a fake.
- `MaskGenerator` returns `IReadOnlyList<DetectedRegion>`; the
  pipeline reuses the list as a backing for the mask bitmap, so
  callers can inspect what the auto-detector found.
- All public methods are async and CancellationToken-aware.

## License

[MIT](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE).
