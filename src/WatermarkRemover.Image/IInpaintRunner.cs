using SkiaSharp;

namespace WatermarkRemover.Image;

/// <summary>
/// Abstraction over the inpainting backend so the pipeline can be unit-tested with a fake
/// implementation (no ONNX model required).
/// </summary>
public interface IInpaintRunner
{
    /// <summary>Model identifier surfaced in results (e.g. "big-lama" or "none").</summary>
    string ModelName { get; }

    /// <summary>True when a usable model is loaded and inference can run.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Inpaint the masked region. <paramref name="image"/> carries RGB(A) pixels,
    /// <paramref name="mask"/> is a single-channel grayscale bitmap where non-zero
    /// marks pixels to reconstruct. Both are at the model resolution. Returns a
    /// new <see cref="SKBitmap"/> of the same size with RGB(A) pixels.
    /// </summary>
    SKBitmap Inpaint(SKBitmap image, SKBitmap mask);
}
