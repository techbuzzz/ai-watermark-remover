using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
/// Unit tests for <see cref="DetectWatermarkTool"/>. Uses a pipeline
/// wired to a no-op mask generator so the test doesn't need an ONNX
/// model or a real image with watermarks.
/// </summary>
public sealed class DetectWatermarkToolTests : IDisposable
{
    private readonly string _tempDir;

    public DetectWatermarkToolTests()
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
    public void DetectWatermark_NoRegions_ReturnsEmptyJsonArray()
    {
        string path = Path.Combine(_tempDir, "clean.png");
        WriteSolidPng(path, 16, 16, new Rgba32(0, 0, 0, 255));

        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);
        TextContentBlock result = DetectWatermarkTool.DetectWatermark(pipeline, AppConfig.Default, path);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void DetectWatermark_MissingFile_ThrowsMcpException()
    {
        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);

        Action act = () => DetectWatermarkTool.DetectWatermark(pipeline, AppConfig.Default, Path.Combine(_tempDir, "nope.png"));

        act.Should().Throw<ModelContextProtocol.McpException>()
            .WithMessage("*Image not found*");
    }

    [Fact]
    public void DetectWatermark_NullPath_ThrowsMcpException()
    {
        ServiceProvider sp = BuildImageHost(out IImageCleaningPipeline pipeline);

        Action act = () => DetectWatermarkTool.DetectWatermark(pipeline, AppConfig.Default, input_path: null!);

        act.Should().Throw<ModelContextProtocol.McpException>()
            .WithMessage("*`input_path` is required*");
    }

    private static ServiceProvider BuildImageHost(out IImageCleaningPipeline pipeline)
    {
        // No-op mask generator: reports zero regions, so the inpaint
        // step is never reached and the test never touches ONNX.
        IMaskGenerator mask = new EmptyMaskGenerator();
        IInpaintRunner runner = new NoopInpaintRunner();
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

    private sealed class EmptyMaskGenerator : IMaskGenerator
    {
        public IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold) => [];
    }

    private sealed class NoopInpaintRunner : IInpaintRunner
    {
        public string ModelName => "noop";
        public bool IsAvailable => true;
        public Image<SixLabors.ImageSharp.PixelFormats.Rgb24> Inpaint(Image<SixLabors.ImageSharp.PixelFormats.Rgb24> image, Image<SixLabors.ImageSharp.PixelFormats.L8> mask) => image.Clone();
    }
}
