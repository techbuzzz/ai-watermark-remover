# WatermarkRemover.Metadata

Byte-level metadata strippers for documents and images. The router
(`FileCleanerRouter`) dispatches on file extension; each format
cleaner is a self-contained class implementing
`IFileMetadataCleaner`.

## Supported formats

| Extension | Cleaner       | Strips                                       |
|-----------|---------------|----------------------------------------------|
| `.jpg` / `.jpeg` | `JpegMetadataCleaner`  | EXIF, XMP, IPTC, ICC, Photoshop, MakerNotes, C2PA |
| `.png`    | `PngMetadataCleaner`  | tEXt, iTXt, zTXt, eXIf, C2PA JUMBF           |
| `.pdf`    | `PdfMetadataCleaner`  | Info dictionary, XMP, AcroForm, embedded files |
| `.docx`   | `DocxMetadataCleaner` | `core.xml`, `app.xml`, custom XML parts      |
| `.html` / `.htm` | `HtmlMetadataCleaner` | `<meta name="author/...">`, generator, comments |
| `.webp`   | `WebpMetadataCleaner` | RIFF EXIF, XMP, ICCP chunks                  |

## Install

```powershell
dotnet add package WatermarkRemover.Metadata
```

## Use

```csharp
using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core;
using WatermarkRemover.Metadata;

var services = new ServiceCollection();
services.AddWatermarkRemoverCore(AppConfig.Default);
services.AddWatermarkRemoverMetadata();
var provider = services.BuildServiceProvider();

IFileCleanerRouter router = provider.GetRequiredService<IFileCleanerRouter>();

// Inspect (dry-run)
IReadOnlyList<MetadataEntry> entries = router.Inspect("photo.jpg");
foreach (var e in entries)
{
    Console.WriteLine($"{e.Key} = {e.Value}");
}

// Clean (writes back to the same path; returns the new file size)
long newSize = router.Clean("photo.jpg");
```

## Project conventions

- Cleaners are **non-destructive** for pixel data — they only touch
  metadata blocks. `IFileMetadataCleaner` returns the cleaned byte
  span, never mutates the input in place.
- Streaming cleaners (PDF, DOCX) are forward-only and bounded by the
  peak file size; the router never buffers the whole file in memory
  unless the format demands it (small files only).

## License

[MIT](https://github.com/techbuzzz/ai-watermark-remover/blob/main/LICENSE).
