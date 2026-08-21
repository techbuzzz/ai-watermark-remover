using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WatermarkRemover.Image;

namespace WatermarkRemover.Image.Tests;

/// <summary>
/// Test double for <see cref="IInpaintRunner"/> — no ONNX model needed. It paints every masked
/// pixel a fixed colour so tests can assert that inpainting actually ran on the masked region.
/// </summary>
internal sealed class FakeInpaintRunner(bool available = true, Rgb24? fill = null) : IInpaintRunner
{
    private readonly Rgb24 _fill = fill ?? new Rgb24(255, 0, 0);

    public string ModelName => "fake";

    public bool IsAvailable { get; } = available;

    public int InpaintCallCount { get; private set; }

    public Image<Rgb24> Inpaint(Image<Rgb24> image, Image<L8> mask)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        InpaintCallCount++;

        Image<Rgb24> output = image.Clone();
        output.ProcessPixelRows(mask, (imgAccessor, maskAccessor) =>
        {
            for (int y = 0; y < imgAccessor.Height; y++)
            {
                Span<Rgb24> imgRow = imgAccessor.GetRowSpan(y);
                Span<L8> maskRow = maskAccessor.GetRowSpan(y);
                for (int x = 0; x < imgRow.Length; x++)
                {
                    if (maskRow[x].PackedValue > 127)
                    {
                        imgRow[x] = _fill;
                    }
                }
            }
        });

        return output;
    }
}
