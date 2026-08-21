using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Mcp.Tools;

/// <summary>
/// MCP tool wrapper around <see cref="IImageCleaningPipeline.Detect"/>.
/// Reports candidate watermark regions in an image without inpainting
/// — useful for "is there a watermark, and where?" workflows that
/// shouldn't mutate the source file.
/// </summary>
[McpServerToolType]
public static class DetectWatermarkTool
{
    [McpServerTool(Name = "detect_watermark")]
    [Description("Detect (without inpainting) visual watermark regions in an image. Returns an array of `DetectedRegion` records with bounding box (x, y, width, height) and confidence. Use this before `clean_image` if you want to confirm where the watermark is and how confident the auto-detector is.")]
    public static TextContentBlock DetectWatermark(
        IImageCleaningPipeline pipeline,
        AppConfig config,
        [Description("Absolute path to the image to inspect (JPEG / PNG / WebP).")] string input_path,
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
        };

        IReadOnlyList<DetectedRegion> regions = pipeline.Detect(input_path, options);
        string json = JsonSerializer.Serialize(regions);
        return new TextContentBlock { Text = json };
    }
}
