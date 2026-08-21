using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
        using var image = new Image<Rgba32>(w, h, new Rgba32(10, 20, 30));
        image.Save(path);
        return path;
    }

    private string CreateWhiteMask(string name, int w = 32, int h = 32)
    {
        string path = Path.Combine(_dir, name);
        using var mask = new Image<L8>(w, h, new L8(255));
        mask.Save(path);
        return path;
    }

    [Fact]
    public async Task CleanAsync_WithExplicitMask_RunsInpaintAndWritesOutput()
    {
        string input = CreateSolidImage("in.png");
        string mask = CreateWhiteMask("mask.png");
        string output = Path.Combine(_dir, "out.png");

        var runner = new FakeInpaintRunner(available: true, fill: new Rgb24(255, 0, 0));
        var pipeline = new ImageCleaningPipeline(new MaskGenerator(), runner);

        ImageCleanResult result = await pipeline.CleanAsync(
            input, output, new ImageCleanOptions { MaskPath = mask, BlendEdges = false });

        runner.InpaintCallCount.Should().Be(1);
        result.ModelUsed.Should().Be("fake");
        File.Exists(output).Should().BeTrue();

        using var outImage = SixLabors.ImageSharp.Image.Load<Rgba32>(output);
        outImage.Width.Should().Be(32);
        // Masked (entire) image should now be dominated by the inpaint fill colour.
        outImage[16, 16].R.Should().BeGreaterThan(200);
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
