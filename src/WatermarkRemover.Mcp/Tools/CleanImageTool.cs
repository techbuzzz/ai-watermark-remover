using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Mcp.Tools;

/// <summary>
/// MCP tool wrapper around <see cref="IImageCleaningPipeline"/>. Removes
/// visual watermarks from an image via the full mask / resize / infer /
/// blend pipeline and returns the cleaned PNG bytes as an MCP image
/// content block. The agent receives an immediately-displayable image
/// instead of a base64 string to decode itself.
/// </summary>
[McpServerToolType]
public static class CleanImageTool
{
    [McpServerTool(Name = "clean_image")]
    [Description("Remove visual watermarks from an image via inpainting. Loads the image, builds a mask (auto-detected unless `mask_path` is given), runs the ONNX inpainting model, blends the result, and returns the cleaned PNG bytes as an MCP `ImageContentBlock`. When the ONNX model is unavailable the image is returned unchanged with a warning in the sidecar summary.")]
    public static async Task<IEnumerable<ContentBlock>> CleanImage(
        IImageCleaningPipeline pipeline,
        AppConfig config,
        ILoggerFactory? loggerFactory,
        [Description("Absolute path to the image to clean (JPEG / PNG / WebP).")] string input_path,
        [Description("Optional path to a pre-built mask PNG (grayscale, white = inpaint). When omitted, the mask is auto-detected from the image.")] string? mask_path = null,
        [Description("Auto-detection confidence threshold in [0, 1]. Higher = fewer regions flagged. Defaults to the configured value (0.4).")] double? threshold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(input_path))
        {
            throw new McpException("`input_path` is required and cannot be empty.");
        }

        if (!File.Exists(input_path))
        {
            throw new McpException($"Image not found: {input_path}");
        }

        ImageCleanOptions options = new()
        {
            ModelPath = config.Image.ModelPath,
            AutoDetectThreshold = threshold ?? config.Image.AutoDetectThreshold,
            BlendEdges = config.Image.BlendEdges,
            MaskPath = mask_path,
        };

        string outputPath = ResolveOutputPath(input_path);
        ImageCleanResult result = await pipeline.CleanAsync(input_path, outputPath, options, cancellationToken).ConfigureAwait(false);

        // The cleaned image is delivered as PNG regardless of the
        // input format so the agent receives a single, predictable
        // MIME type. We re-encode via ImageSharp to avoid leaking the
        // original's metadata and to keep the byte layout consistent.
        byte[] cleanedPng = await EncodePngAsync(result.OutputPath, cancellationToken).ConfigureAwait(false);

        string summary = JsonSerializer.Serialize(new
        {
            inputPath = result.InputPath,
            outputPath = result.OutputPath,
            detectedRegions = result.DetectedWatermarks,
            inputSize = new { width = result.InputWidth, height = result.InputHeight },
            outputSize = new { width = result.OutputWidth, height = result.OutputHeight },
            processingTimeMs = result.ProcessingTime.TotalMilliseconds,
            modelUsed = result.ModelUsed,
        });

        ILogger? log = loggerFactory?.CreateLogger("WatermarkRemover.Mcp.CleanImageTool");
        if (string.Equals(result.ModelUsed, "none", StringComparison.Ordinal))
        {
            // Surface the graceful-degradation path to the operator
            // via a log line — agents don't see the host's stderr but
            // the in-MCP-session log channel does forward to clients
            // that opt in.
            log?.LogWarning(
                "ONNX inpainting model unavailable; returning {Input} unchanged. Run 'download-model' to enable visual watermark removal.",
                input_path);
        }

        return new ContentBlock[]
        {
            new TextContentBlock { Text = summary },
            ImageContentBlock.FromBytes(cleanedPng, "image/png"),
        };
    }

    private static string ResolveOutputPath(string inputPath)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{name}.cleaned.png");
    }

    private static async Task<byte[]> EncodePngAsync(string path, CancellationToken cancellationToken)
    {
        // Use the global:: qualifier to disambiguate from the
        // WatermarkRemover.Image namespace exposed by the same project
        // reference — both define an `Image` symbol and the compiler
        // resolves to the project's namespace by default.
        using Image<Rgba32> image = await global::SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path, cancellationToken).ConfigureAwait(false);
        using MemoryStream ms = new();
        await image.SaveAsync(ms, new PngEncoder(), cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
