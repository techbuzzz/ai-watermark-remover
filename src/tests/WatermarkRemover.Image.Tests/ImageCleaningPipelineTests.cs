using System.Runtime.InteropServices;
using FluentAssertions;
using SkiaSharp;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Image;
using Xunit;

namespace WatermarkRemover.Image.Tests;

public class ImageCleaningPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wr-img-tests", Guid.NewGuid().ToString("N"));

    public ImageCleaningPipelineTests() => Directory.CreateDirectory(_dir);

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

    private string CreateSolidImage(string name, int w = 32, int h = 32)
    {
        string path = Path.Combine(_dir, name);
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKColor color = new(10, 20, 30);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        pixels.Fill(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
        return path;
    }

    private string CreateWhiteMask(string name, int w = 32, int h = 32)
    {
        string path = Path.Combine(_dir, name);
        using var mask = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        mask.GetPixelSpan().Fill((byte)255);
        using var image = SKImage.FromBitmap(mask);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
        return path;
    }

    [Fact]
    public async Task CleanAsync_WithExplicitMask_RunsInpaintAndWritesOutput()
    {
        string input = CreateSolidImage("in.png");
        string mask = CreateWhiteMask("mask.png");
        string output = Path.Combine(_dir, "out.png");

        var runner = new FakeInpaintRunner(available: true, fill: new SKColor(255, 0, 0));
        var pipeline = new ImageCleaningPipeline(new MaskGenerator(), runner);

        ImageCleanResult result = await pipeline.CleanAsync(
            input, output, new ImageCleanOptions { MaskPath = mask, BlendEdges = false });

        runner.InpaintCallCount.Should().Be(1);
        result.ModelUsed.Should().Be("fake");
        File.Exists(output).Should().BeTrue();

        using var outBitmap = SKBitmap.Decode(output);
        outBitmap.Width.Should().Be(32);
        // Masked (entire) image should now be dominated by the inpaint fill colour.
        // SkiaSharp decodes the PNG as BGRA-8888 (its default byte order
        // on Windows). To avoid the BGRA/RGBA byte-swap ambiguity when
        // reading the pixel value, we force the decoded bitmap into the
        // RGBA-8888 colour type the SKColor struct's byte layout assumes.
        using var rgba = outBitmap.ColorType == SKColorType.Rgba8888
            ? outBitmap
            : outBitmap.Copy(SKColorType.Rgba8888);
        ReadOnlySpan<SKColor> outPixels = MemoryMarshal.Cast<byte, SKColor>(rgba.GetPixelSpan());
        SKColor center = outPixels[(16 * 32) + 16];
        center.Red.Should().BeGreaterThan((byte)200);
    }

    [Fact]
    public async Task CleanAsync_ModelUnavailable_DegradesGracefully()
    {
        string input = CreateSolidImage("in.png");
        string mask = CreateWhiteMask("mask.png");
        string output = Path.Combine(_dir, "out.png");

        var runner = new FakeInpaintRunner(available: false);
        var pipeline = new ImageCleaningPipeline(new MaskGenerator(), runner);

        ImageCleanResult result = await pipeline.CleanAsync(
            input, output, new ImageCleanOptions { MaskPath = mask });

        runner.InpaintCallCount.Should().Be(0);
        result.ModelUsed.Should().Be("none");
        File.Exists(output).Should().BeTrue();
    }

    [Fact]
    public async Task CleanAsync_MissingInput_Throws()
    {
        var pipeline = new ImageCleaningPipeline(new MaskGenerator(), new FakeInpaintRunner());
        Func<Task> act = async () => await pipeline.CleanAsync(
            Path.Combine(_dir, "nope.png"), Path.Combine(_dir, "o.png"), new ImageCleanOptions());

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task CleanAsync_NoRegionsDetected_CopiesUnchanged()
    {
        // Solid image with no distinctive regions and no explicit mask -> nothing to inpaint.
        string input = CreateSolidImage("plain.png");
        string output = Path.Combine(_dir, "out.png");

        var runner = new FakeInpaintRunner(available: true);
        var pipeline = new ImageCleaningPipeline(new MaskGenerator(), runner);

        ImageCleanResult result = await pipeline.CleanAsync(input, output, new ImageCleanOptions());

        runner.InpaintCallCount.Should().Be(0);
        File.Exists(output).Should().BeTrue();
        result.OutputWidth.Should().Be(32);
    }
}
