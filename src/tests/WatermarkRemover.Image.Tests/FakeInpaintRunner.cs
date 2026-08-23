using System.Runtime.InteropServices;
using SkiaSharp;
using WatermarkRemover.Image;

namespace WatermarkRemover.Image.Tests;

/// <summary>
/// Test double for <see cref="IInpaintRunner"/> — no ONNX model needed. It paints every masked
/// pixel a fixed colour so tests can assert that inpainting actually ran on the masked region.
/// </summary>
internal sealed class FakeInpaintRunner(bool available = true, SKColor? fill = null) : IInpaintRunner
{
    private readonly SKColor _fill = fill ?? new SKColor(255, 0, 0);

    public string ModelName => "fake";

    public bool IsAvailable { get; } = available;

    public int InpaintCallCount { get; private set; }

    public SKBitmap Inpaint(SKBitmap image, SKBitmap mask)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        InpaintCallCount++;

        // The pipeline hands us an Rgb888x (3-channel) image and a Gray8
        // mask. We mirror the input colour type so the test's expectations
        // about per-pixel accessors (Rgb24 / Rgb888x) keep working.
        SKBitmap output = new(image.Width, image.Height, image.ColorType, SKAlphaType.Opaque);
        ReadOnlySpan<SKColor> inputPixels = MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
        ReadOnlySpan<byte> maskPixels = mask.GetPixelSpan();
        Span<SKColor> outPixels = MemoryMarshal.Cast<byte, SKColor>(output.GetPixelSpan());

        for (int i = 0; i < inputPixels.Length; i++)
        {
            outPixels[i] = maskPixels[i] > 127 ? _fill : inputPixels[i];
        }

        return output;
    }
}
