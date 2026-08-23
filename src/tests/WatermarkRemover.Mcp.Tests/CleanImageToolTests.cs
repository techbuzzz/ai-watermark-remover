using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using SkiaSharp;
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
        WriteSolidPng(inputPath, 32, 32, new SKColor(255, 0, 0, 255));

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
        var runner = new FakeInpaintRunner(available: true, fill: new SKColor(0, 255, 0));
        IMaskGenerator mask = new NoOpMaskGenerator();
        pipeline = new ImageCleaningPipeline(mask, runner, logger: null);

        ServiceCollection services = new();
        services.AddSingleton(pipeline);
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }

    private static void WriteSolidPng(string path, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        pixels.Fill(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
    }

    /// <summary>Mask generator that reports no regions — keeps the inpaint step a no-op.</summary>
    private sealed class NoOpMaskGenerator : IMaskGenerator
    {
        public IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold) => [];
    }

    /// <summary>In-process IInpaintRunner that paints every masked pixel a fixed color.</summary>
    private sealed class FakeInpaintRunner(bool available, SKColor fill) : IInpaintRunner
    {
        public string ModelName => "fake";
        public bool IsAvailable { get; } = available;
        public int InpaintCallCount { get; private set; }

        public SKBitmap Inpaint(SKBitmap image, SKBitmap mask)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(mask);
            InpaintCallCount++;

            SKBitmap output = new(image.Width, image.Height, image.ColorType, SKAlphaType.Opaque);
            ReadOnlySpan<SKColor> inputPixels = MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
            ReadOnlySpan<byte> maskPixels = mask.GetPixelSpan();
            Span<SKColor> outPixels = MemoryMarshal.Cast<byte, SKColor>(output.GetPixelSpan());

            for (int i = 0; i < inputPixels.Length; i++)
            {
                outPixels[i] = maskPixels[i] > 127 ? fill : inputPixels[i];
            }
            return output;
        }
    }
}
