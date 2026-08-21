using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Image;
using WatermarkRemover.Mcp.Tools;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// Unit tests for <see cref="CleanImageTool"/>. Builds an in-memory
/// pipeline with a <c>FakeInpaintRunner</c> so the test never needs
/// the ONNX model.
/// </summary>
public sealed class CleanImageToolTests : IDisposable
{
    private readonly string _tempDir;

    public CleanImageToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wr-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CleanImage_ReturnsTextAndImageBlocks()
    {
        // Arrange — write a 32x32 red PNG as input.
        string inputPath = Path.Combine(_tempDir, "fixture.png");
        WriteSolidPng(inputPath, 32, 32, new Rgba32(255, 0, 0, 255));

        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);
        AppConfig config = AppConfig.Default;

        // Act
        IEnumerable<ContentBlock> blocks = await CleanImageTool.CleanImage(pipeline, config, loggerFactory: null, input_path: inputPath);

        // Assert — two blocks: JSON sidecar + ImageContentBlock.
        ContentBlock[] arr = blocks.ToArray();
        arr.Should().HaveCount(2);
        arr[0].Should().BeOfType<TextContentBlock>();
        arr[1].Should().BeOfType<ImageContentBlock>();

        TextContentBlock summary = (TextContentBlock)arr[0];
        using JsonDocument doc = JsonDocument.Parse(summary.Text);
        doc.RootElement.GetProperty("inputPath").GetString().Should().Be(inputPath);
        doc.RootElement.GetProperty("modelUsed").GetString().Should().NotBeNullOrEmpty();

        ImageContentBlock image = (ImageContentBlock)arr[1];
        image.MimeType.Should().Be("image/png");
        image.DecodedData.Length.Should().BeGreaterThan(0);
        // First 8 bytes of a PNG file are always the same signature.
        byte[] bytes = image.DecodedData.ToArray();
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50); // 'P'
        bytes[2].Should().Be(0x4E); // 'N'
        bytes[3].Should().Be(0x47); // 'G'
    }

    [Fact]
    public async Task CleanImage_MissingFile_ThrowsMcpException()
    {
        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);

        Func<Task> act = () => CleanImageTool.CleanImage(pipeline, AppConfig.Default, loggerFactory: null, input_path: Path.Combine(_tempDir, "nope.png"));

        await act.Should().ThrowAsync<ModelContextProtocol.McpException>()
            .WithMessage("*Image not found*");
    }

    [Fact]
    public async Task CleanImage_NullPath_ThrowsMcpException()
    {
        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);

        Func<Task> act = () => CleanImageTool.CleanImage(pipeline, AppConfig.Default, loggerFactory: null, input_path: null!);

        await act.Should().ThrowAsync<ModelContextProtocol.McpException>()
            .WithMessage("*`input_path` is required*");
    }

    private static ServiceProvider BuildImageHost(out IImageCleaningPipeline pipeline)
    {
        // Local fake runner — same shape as WatermarkRemover.Image.Tests.FakeInpaintRunner
        // but duplicated here to avoid making the test project reach
        // into another test assembly's internal types.
        var runner = new FakeInpaintRunner(available: true, fill: new SixLabors.ImageSharp.PixelFormats.Rgb24(0, 255, 0));
        IMaskGenerator mask = new NoOpMaskGenerator();
        pipeline = new ImageCleaningPipeline(mask, runner, logger: null);

        ServiceCollection services = new();
        services.AddSingleton(pipeline);
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }

    private static void WriteSolidPng(string path, int width, int height, Rgba32 color)
    {
        using Image<Rgba32> image = new(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = color;
                }
            }
        });
        image.Save(path);
    }

    /// <summary>Mask generator that reports no regions — keeps the inpaint step a no-op.</summary>
    private sealed class NoOpMaskGenerator : IMaskGenerator
    {
        public IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold) => [];
    }

    /// <summary>In-process IInpaintRunner that paints every masked pixel a fixed color.</summary>
    private sealed class FakeInpaintRunner : IInpaintRunner
    {
        private readonly SixLabors.ImageSharp.PixelFormats.Rgb24 _fill;
        public FakeInpaintRunner(bool available, SixLabors.ImageSharp.PixelFormats.Rgb24 fill)
        {
            IsAvailable = available;
            _fill = fill;
        }
        public string ModelName => "fake";
        public bool IsAvailable { get; }
        public int InpaintCallCount { get; private set; }
        public Image<SixLabors.ImageSharp.PixelFormats.Rgb24> Inpaint(Image<SixLabors.ImageSharp.PixelFormats.Rgb24> image, Image<SixLabors.ImageSharp.PixelFormats.L8> mask)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(mask);
            InpaintCallCount++;
            Image<SixLabors.ImageSharp.PixelFormats.Rgb24> output = image.Clone();
            output.ProcessPixelRows(mask, (imgAccessor, maskAccessor) =>
            {
                for (int y = 0; y < imgAccessor.Height; y++)
                {
                    Span<SixLabors.ImageSharp.PixelFormats.Rgb24> imgRow = imgAccessor.GetRowSpan(y);
                    Span<SixLabors.ImageSharp.PixelFormats.L8> maskRow = maskAccessor.GetRowSpan(y);
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
}
