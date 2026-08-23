using System.Runtime.InteropServices;
using FluentAssertions;
using SkiaSharp;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Image;
using Xunit;

namespace WatermarkRemover.Image.Tests;

public class MaskGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wr-mask-tests", Guid.NewGuid().ToString("N"));

    public MaskGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // ignore
        }
    }

    private static SKBitmap MakeRgba(int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKColor color = new(r, g, b, a);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        pixels.Fill(color);
        return bitmap;
    }

    private string SaveBitmap(SKBitmap bitmap, string name)
    {
        string path = Path.Combine(_dir, name);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
        return path;
    }

    [Fact]
    public void Detect_SolidImage_ReturnsNoRegions()
    {
        using var bitmap = MakeRgba(48, 48, 120, 120, 120);
        string path = SaveBitmap(bitmap, "solid.png");

        var generator = new MaskGenerator();
        IReadOnlyList<DetectedRegion> regions = generator.Detect(path, 0.4);

        regions.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ImageWithTransparentRegion_IsDetected()
    {
        using var bitmap = MakeRgba(48, 48, 200, 200, 200);
        // Punch a semi-transparent block (classic watermark overlay signature).
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        for (int y = 10; y < 30; y++)
        {
            for (int x = 10; x < 30; x++)
            {
                pixels[(y * 48) + x] = new SKColor(255, 255, 255, 90);
            }
        }
        string path = SaveBitmap(bitmap, "alpha.png");

        var generator = new MaskGenerator();
        IReadOnlyList<DetectedRegion> regions = generator.Detect(path, 0.3);

        regions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildMask_SolidImage_ReturnsZeroCount()
    {
        using var image = MakeRgba(16, 16, 50, 50, 50);
        (bool[,] _, int count) = MaskGenerator.BuildMask(image);

        count.Should().Be(0);
    }
}
