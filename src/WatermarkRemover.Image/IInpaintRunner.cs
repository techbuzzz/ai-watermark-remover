using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    /// Inpaint the masked region. <paramref name="image"/> is RGB, <paramref name="mask"/> is
    /// grayscale where non-zero marks pixels to reconstruct. Both are the model resolution.
    /// Returns a new inpainted RGB image at the same resolution.
    /// </summary>
    Image<Rgb24> Inpaint(Image<Rgb24> image, Image<L8> mask);
}
