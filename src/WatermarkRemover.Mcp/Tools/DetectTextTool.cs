using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Mcp.Tools;

/// <summary>
/// MCP tool wrapper around <see cref="ITextCleaningPipeline.Detect"/>.
/// Reports (without removing) every vendor watermark signature found
/// in the input — useful for inspection / audit workflows where the
/// caller wants to know what's there before deciding to clean.
/// </summary>
[McpServerToolType]
public static class DetectTextTool
{
    [McpServerTool(Name = "detect_text")]
    [Description("Detect (without removing) AI vendor watermark signatures in plain text. Returns the full list of `WatermarkMatch` records with vendor, pattern, position, length, and confidence.")]
    public static TextContentBlock DetectText(
        ITextCleaningPipeline pipeline,
        [Description("The text payload to inspect.")] string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (text is null)
        {
            throw new McpException("`text` is required and cannot be null.");
        }

        IReadOnlyList<WatermarkMatch> matches = pipeline.Detect(text);
        string json = JsonSerializer.Serialize(matches);
        return new TextContentBlock { Text = json };
    }
}
