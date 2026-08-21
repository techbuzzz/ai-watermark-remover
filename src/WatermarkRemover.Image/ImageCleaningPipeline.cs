using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Image;

/// <summary>
/// Orchestrates visual watermark removal: load → mask → resize → infer → blend → save.
/// Degrades gracefully when the ONNX model is unavailable (logs a warning and returns the
/// original image unchanged).
/// </summary>
public sealed class ImageCleaningPipeline(
    IMaskGenerator maskGenerator,
    IInpaintRunner inpaintRunner,
    ILogger<ImageCleaningPipeline>? logger = null) : IImageCleaningPipeline
{
    private readonly IMaskGenerator _maskGenerator = maskGenerator;
    private readonly IInpaintRunner _inpaintRunner = inpaintRunner;
    private readonly ILogger _logger = logger ?? NullLogger<ImageCleaningPipeline>.Instance;

    /// <inheritdoc />
    public IReadOnlyList<DetectedRegion> Detect(string inputPath, ImageCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _maskGenerator.Detect(inputPath, options.AutoDetectThreshold);
    }

    /// <inheritdoc />
    public async Task<ImageCleanResult> CleanAsync(string inputPath, string outputPath, ImageCleanOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input image not found", inputPath);
        }

        var sw = Stopwatch.StartNew();
        using Image<Rgba32> original = SixLabors.ImageSharp.Image.Load<Rgba32>(inputPath);
        int inW = original.Width;
        int inH = original.Height;

        // 1. Build mask + detect regions.
        (Image<L8> maskFull, IReadOnlyList<DetectedRegion> regions) = BuildMask(original, options);
        using Image<L8> maskDisposable = maskFull;

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;

        int maskedPixels = CountMasked(maskDisposable);
        if (maskedPixels == 0)
        {
            _logger.LogInformation("No watermark regions detected in {Input}; copying unchanged.", inputPath);
            await SaveAsync(original, finalOut, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new ImageCleanResult(inputPath, finalOut, regions, inW, inH, inW, inH, sw.Elapsed, _inpaintRunner.ModelName);
        }

        // 2. Graceful degradation when the model is missing.
        if (!_inpaintRunner.IsAvailable)
        {
            _logger.LogWarning(
                "ONNX inpainting model unavailable; skipping inpainting for {Input}. Run 'download-model' to enable visual watermark removal.",
                inputPath);
            await SaveAsync(original, finalOut, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new ImageCleanResult(inputPath, finalOut, regions, inW, inH, inW, inH, sw.Elapsed, "none");
        }

        // 3. Prepare model-resolution inputs.
        int res = options.ModelResolution;
        using Image<Rgb24> rgb = original.CloneAs<Rgb24>();
        using Image<Rgb24> rgbResized = rgb.Clone(ctx => ctx.Resize(res, res));
        using Image<L8> maskResized = maskDisposable.Clone(ctx => ctx.Resize(res, res));

        // 4. Inference.
        using Image<Rgb24> inpainted = _inpaintRunner.Inpaint(rgbResized, maskResized);

        // 5. Resize inpainted back to original resolution.
        using Image<Rgb24> inpaintedFull = inpainted.Clone(ctx => ctx.Resize(inW, inH));

        // 6. Blend (soft edges optional).
        using Image<L8> blendMask = BuildBlendMask(maskDisposable, options.BlendEdges);
        using Image<Rgba32> result = Blend(original, inpaintedFull, blendMask);

        // 7. Save.
        await SaveAsync(result, finalOut, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return new ImageCleanResult(inputPath, finalOut, regions, inW, inH, inW, inH, sw.Elapsed, _inpaintRunner.ModelName);
    }

    private (Image<L8> Mask, IReadOnlyList<DetectedRegion> Regions) BuildMask(Image<Rgba32> original, ImageCleanOptions options)
    {
        if (!string.IsNullOrEmpty(options.MaskPath))
        {
            if (!File.Exists(options.MaskPath))
            {
                throw new FileNotFoundException("Mask image not found", options.MaskPath);
            }

            Image<L8> loaded = SixLabors.ImageSharp.Image.Load<L8>(options.MaskPath);
            if (loaded.Width != original.Width || loaded.Height != original.Height)
            {
                loaded.Mutate(ctx => ctx.Resize(original.Width, original.Height));
            }

            var region = new DetectedRegion(0, 0, original.Width, original.Height, 1.0);
            return (loaded, [region]);
        }

        // Single pass: build the boolean mask and extract regions together.
        // The previous code called MaskGenerator.BuildMask (which internally
        // ran ExtractRegions and threw the result away) and then re-scanned
        // the whole 2D bool array here to derive bounding boxes — doubling
        // the O(W*H) work on every image.
        (bool[,] boolMask, _, IReadOnlyList<DetectedRegion> regions) =
            MaskGenerator.BuildMaskWithRegions(original, options.AutoDetectThreshold);

        var mask = new Image<L8>(original.Width, original.Height);
        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L8> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (boolMask[y, x])
                    {
                        row[x] = new L8(255);
                    }
                }
            }
        });

        return (mask, regions);
    }

    private static Image<L8> BuildBlendMask(Image<L8> mask, bool softEdges)
    {
        Image<L8> blend = mask.Clone();
        if (softEdges)
        {
            blend.Mutate(ctx => ctx.GaussianBlur(2f));
        }

        return blend;
    }

    private static Image<Rgba32> Blend(Image<Rgba32> original, Image<Rgb24> inpainted, Image<L8> blendMask)
    {
        var result = original.Clone();
        // ProcessPixelRows has an overload that synchronises row access across
        // multiple images in one call — far cheaper than calling `image[x,y]`
        // per pixel, which does a per-access row-lock + coordinate clamp. We
        // pass the result, the inpainted source and the blend mask together so
        // each iteration only touches stack spans.
        result.ProcessPixelRows(inpainted, blendMask, (acc, inpAcc, maskAcc) =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                Span<Rgba32> row = acc.GetRowSpan(y);
                ReadOnlySpan<Rgb24> inpRow = inpAcc.GetRowSpan(y);
                ReadOnlySpan<L8> maskRow = maskAcc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float a = maskRow[x].PackedValue / 255f;
                    if (a <= 0f)
                    {
                        continue;
                    }

                    Rgb24 rep = inpRow[x];
                    Rgba32 orig = row[x];
                    row[x] = new Rgba32(
                        (byte)Math.Clamp((orig.R * (1 - a)) + (rep.R * a), 0, 255),
                        (byte)Math.Clamp((orig.G * (1 - a)) + (rep.G * a), 0, 255),
                        (byte)Math.Clamp((orig.B * (1 - a)) + (rep.B * a), 0, 255),
                        (byte)255);
                }
            }
        });

        return result;
    }

    private static int CountMasked(Image<L8> mask)
    {
        int count = 0;
        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L8> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].PackedValue > 127)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }

    private static async Task SaveAsync<TPixel>(Image<TPixel> image, string path, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await image.SaveAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
