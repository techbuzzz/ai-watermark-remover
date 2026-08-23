using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        WriteSolidPng(path, 16, 16, new SKColor(0, 0, 0, 255));

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

    private sealed class EmptyMaskGenerator : IMaskGenerator
    {
        public IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold) => [];
    }

    private sealed class NoopInpaintRunner : IInpaintRunner
    {
        public string ModelName => "noop";
        public bool IsAvailable => true;

        public SKBitmap Inpaint(SKBitmap image, SKBitmap mask)
        {
            // In a no-op runner we still need to honour the contract
            // and return a brand-new bitmap the caller can dispose.
            // A Copy preserves the colour type the pipeline handed us.
            return image.Copy(image.ColorType);
        }
    }
}
