using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

    [Fact]
    public void Detect_SolidImage_ReturnsNoRegions()
    {
        string path = Path.Combine(_dir, "solid.png");
        using (var image = new Image<Rgba32>(48, 48, new Rgba32(120, 120, 120)))
        {
            image.Save(path);
        }

        var generator = new MaskGenerator();
        IReadOnlyList<DetectedRegion> regions = generator.Detect(path, 0.4);

        regions.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ImageWithTransparentRegion_IsDetected()
    {
        string path = Path.Combine(_dir, "alpha.png");
        using (var image = new Image<Rgba32>(48, 48, new Rgba32(200, 200, 200, 255)))
        {
            // Punch a semi-transparent block (classic watermark overlay signature).
            for (int y = 10; y < 30; y++)
            {
                for (int x = 10; x < 30; x++)
                {
                    image[x, y] = new Rgba32(255, 255, 255, 90);
                }
            }

            image.Save(path);
        }

        var generator = new MaskGenerator();
        IReadOnlyList<DetectedRegion> regions = generator.Detect(path, 0.3);

        regions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildMask_SolidImage_ReturnsZeroCount()
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(50, 50, 50));
        (bool[,] _, int count) = MaskGenerator.BuildMask(image);

        count.Should().Be(0);
    }
}
