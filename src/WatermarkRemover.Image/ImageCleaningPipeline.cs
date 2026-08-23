using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
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
        using SKBitmap original = LoadRgba(inputPath);
        int inW = original.Width;
        int inH = original.Height;

        // 1. Build mask + detect regions.
        (SKBitmap maskFull, IReadOnlyList<DetectedRegion> regions) = BuildMask(original, options);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;

        int maskedPixels = CountMasked(maskFull);
        if (maskedPixels == 0)
        {
            maskFull.Dispose();
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
            maskFull.Dispose();
            await SaveAsync(original, finalOut, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new ImageCleanResult(inputPath, finalOut, regions, inW, inH, inW, inH, sw.Elapsed, "none");
        }

        // 3. Prepare model-resolution inputs.
        int res = options.ModelResolution;
        using SKBitmap rgb = ToRgb(original);
        using SKBitmap rgbResized = Resize(rgb, res, res);
        using SKBitmap maskResized = Resize(maskFull, res, res);
        // maskFull stays alive until the blend step — BuildBlendMask
        // operates on the full-resolution mask, not the model-resolution
        // one (the latter was a porting mistake that produced almost-zero
        // blend weights when the source image was tiny).

        // 4. Inference.
        using SKBitmap inpainted = _inpaintRunner.Inpaint(rgbResized, maskResized);

        // 5. Resize inpainted back to original resolution.
        using SKBitmap inpaintedFull = Resize(inpainted, inW, inH);

        // 6. Blend (soft edges optional).
        using SKBitmap blendMask = BuildBlendMask(maskFull, options.BlendEdges, inW, inH);
        using SKBitmap result = Blend(original, inpaintedFull, blendMask);
        maskFull.Dispose();

        // 7. Save.
        await SaveAsync(result, finalOut, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return new ImageCleanResult(inputPath, finalOut, regions, inW, inH, inW, inH, sw.Elapsed, _inpaintRunner.ModelName);
    }

    private (SKBitmap Mask, IReadOnlyList<DetectedRegion> Regions) BuildMask(SKBitmap original, ImageCleanOptions options)
    {
        if (!string.IsNullOrEmpty(options.MaskPath))
        {
            if (!File.Exists(options.MaskPath))
            {
                throw new FileNotFoundException("Mask image not found", options.MaskPath);
            }

            SKBitmap loaded = LoadGray(options.MaskPath);
            if (loaded.Width != original.Width || loaded.Height != original.Height)
            {
                SKBitmap resized = Resize(loaded, original.Width, original.Height);
                loaded.Dispose();
                loaded = resized;
            }

            var region = new DetectedRegion(0, 0, original.Width, original.Height, 1.0);
            return (loaded, [region]);
        }

        // Single pass: build the boolean mask and extract regions together.
        (bool[,] boolMask, _, IReadOnlyList<DetectedRegion> regions) =
            MaskGenerator.BuildMaskWithRegions(original, options.AutoDetectThreshold);

        SKBitmap mask = new(original.Width, original.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        Span<byte> maskPixels = mask.GetPixelSpan();
        int width = original.Width;
        int height = original.Height;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                if (boolMask[y, x])
                {
                    maskPixels[rowOffset + x] = 255;
                }
            }
        }

        return (mask, regions);
    }

    private static SKBitmap BuildBlendMask(SKBitmap mask, bool softEdges, int targetW, int targetH)
    {
        // The mask passed in is already at the full image resolution;
        // when softEdges is false we just hand the caller a fresh copy
        // they can dispose independently. When softEdges is true we
        // apply a small Gaussian blur to feather the mask boundary.
        if (!softEdges)
        {
            // Same-dimension copy: avoids SkiaSharp's Resize path
            // attenuating high-contrast values (the bilinear / bicubic
            // samplers can pull a 255 mask pixel down to 0 when
            // src == dst, depending on rounding).
            return mask.Copy(mask.ColorType);
        }

        var blurred = new SKBitmap(targetW, targetH, mask.ColorType, mask.AlphaType);
        using (var canvas = new SKCanvas(blurred))
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(2f, 2f),
            };
            canvas.DrawBitmap(mask, 0, 0, paint);
        }

        return blurred;
    }

    private static SKBitmap Blend(SKBitmap original, SKBitmap inpainted, SKBitmap blendMask)
    {
        var result = new SKBitmap(original.Width, original.Height, SKColorType.Rgba8888, original.AlphaType);
        ReadOnlySpan<SKColor> origPixels = MemoryMarshal.Cast<byte, SKColor>(original.GetPixelSpan());
        ReadOnlySpan<SKColor> inpPixels = MemoryMarshal.Cast<byte, SKColor>(inpainted.GetPixelSpan());
        ReadOnlySpan<byte> maskPixels = blendMask.GetPixelSpan();
        Span<SKColor> resultPixels = MemoryMarshal.Cast<byte, SKColor>(result.GetPixelSpan());
        int length = origPixels.Length;

        for (int i = 0; i < length; i++)
        {
            float a = maskPixels[i] / 255f;
            if (a <= 0f)
            {
                resultPixels[i] = origPixels[i];
                continue;
            }

            SKColor orig = origPixels[i];
            SKColor rep = inpPixels[i];
            byte r = (byte)Math.Clamp((orig.Red * (1 - a)) + (rep.Red * a), 0, 255);
            byte g = (byte)Math.Clamp((orig.Green * (1 - a)) + (rep.Green * a), 0, 255);
            byte b = (byte)Math.Clamp((orig.Blue * (1 - a)) + (rep.Blue * a), 0, 255);
            resultPixels[i] = new SKColor(r, g, b, 255);
        }

        return result;
    }

    private static int CountMasked(SKBitmap mask)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = mask.GetPixelSpan();
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] > 127)
            {
                count++;
            }
        }
        return count;
    }

    private static async Task SaveAsync(SKBitmap image, string path, CancellationToken cancellationToken)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using SKImage skImage = SKImage.FromBitmap(image);
        SKEncodedImageFormat format = PickFormat(path);
        using SKData data = skImage.Encode(format, 95);
        await using FileStream stream = File.Create(path);
        await data.AsStream().CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static SKEncodedImageFormat PickFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".gif" => SKEncodedImageFormat.Gif,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png,
        };
    }

    /// <summary>Decode an image from <paramref name="path"/> and return it as an
    /// RGBA-8888 <see cref="SKBitmap"/>.</summary>
    private static SKBitmap LoadRgba(string path)
    {
        SKBitmap raw = SKBitmap.Decode(path);
        if (raw.ColorType == SKColorType.Rgba8888)
        {
            return raw;
        }

        SKBitmap converted = raw.Copy(SKColorType.Rgba8888);
        raw.Dispose();
        return converted;
    }

    /// <summary>Decode an image from <paramref name="path"/> and return it as a
    /// single-channel grayscale <see cref="SKBitmap"/>.</summary>
    private static SKBitmap LoadGray(string path)
    {
        SKBitmap raw = SKBitmap.Decode(path);
        if (raw.ColorType == SKColorType.Gray8)
        {
            return raw;
        }

        SKBitmap converted = raw.Copy(SKColorType.Gray8);
        raw.Dispose();
        return converted;
    }

    /// <summary>Convert an RGBA bitmap to RGB-888x (drops alpha, preserves RGB).</summary>
    private static SKBitmap ToRgb(SKBitmap rgba)
    {
        if (rgba.ColorType == SKColorType.Rgb888x)
        {
            return rgba.Copy(SKColorType.Rgb888x);
        }

        var rgb = new SKBitmap(rgba.Width, rgba.Height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        ReadOnlySpan<SKColor> src = MemoryMarshal.Cast<byte, SKColor>(rgba.GetPixelSpan());
        Span<byte> dstBytes = rgb.GetPixelSpan();
        int length = src.Length;
        for (int i = 0; i < length; i++)
        {
            SKColor c = src[i];
            int o = i * 4;
            dstBytes[o] = (byte)c.Red;
            dstBytes[o + 1] = (byte)c.Green;
            dstBytes[o + 2] = (byte)c.Blue;
            dstBytes[o + 3] = 0;
        }
        return rgb;
    }

    /// <summary>Resize a bitmap, preserving its colour type. Returns a new bitmap the caller must dispose.
    /// When the requested dimensions match the source we <see cref="SKBitmap.Copy"/> instead of
    /// going through <see cref="SKBitmap.Resize"/>: SkiaSharp's Resize is not guaranteed to
    /// preserve every pixel when the source and target dimensions are identical (the bilinear /
    /// bicubic samplers can attenuate high-contrast values), and the pipeline relies on the
    /// mask passing through unchanged at the model resolution when the source is already at the
    /// model resolution.</summary>
    private static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return source.Copy(source.ColorType);
        }
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, SKSamplingOptions.Default);
    }
}
