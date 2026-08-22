# WatermarkRemover.Text

The text-cleaning pipeline: invisible-character stripping, statistical
rewrites, and vendor watermark detection (Claude, Gemini, OpenAI) for
plain text and markdown. The pipeline is layered — Layer A is
Unicode hygiene, Layer B is statistical paraphrase, Layer C is the
vendor-specific detectors — and each layer is independently
configurable.

## What's in this package

- `ITextCleaningPipeline` / `TextCleaningPipeline` — composes the layers.
- `IUnicodeHygieneCleaner` / `UnicodeHygieneCleaner` — Layer A: strips
  ZWSP / ZWNJ / homoglyphs / invisible format characters.
- `IStatisticalWatermarkRewriter` / `StatisticalWatermarkRewriter` — Layer B:
  synonym-dictionary paraphrase with optional LLM back-translation.
- `ClaudeWatermarkDetector`, `GeminiWatermarkDetector`,
  `OpenAiWatermarkDetector` — Layer C vendor detectors.
- `IMarkdownCleaner` / `MarkdownCleaner` — 21-toggle markdown sanitiser
  (frontmatter, AI signatures, formatting tidy-ups).
- `SynonymDictionary` — loadable EN/RU synonym sets.

## Install

```powershell
dotnet add package WatermarkRemover.Text
```

## Use

```csharp
using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core;
using WatermarkRemover.Text;

var services = new ServiceCollection();
services.AddWatermarkRemoverCore(AppConfig.Default);
services.AddWatermarkRemoverText();
var provider = services.BuildServiceProvider();

ITextCleaningPipeline pipeline = provider.GetRequiredService<ITextCleaningPipeline>();
TextCleanResult result = await pipeline.CleanAsync("Hello\u200B world");
Console.WriteLine(result.Cleaned);                 // "Hello world"
Console.WriteLine(result.Matches.Count);          // 0 (or 1 if ZWSP matches a vendor pattern)
```

For markdown:

```csharp
IMarkdownCleaner md = provider.GetRequiredService<IMarkdownCleaner>();
MarkdownCleanResult mdResult = md.Clean(
    "# Title\n<!-- AI-generated: yes -->\nbody",
    MarkdownCleanOptions.Default);
```

## Project conventions

- Synchronous for `IMarkdownCleaner` (no I/O); async for the text
  pipeline (the LLM back-translation layer is network-bound).
- All cleaners are thread-safe; register them as singletons.

## License

[MIT](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE).
