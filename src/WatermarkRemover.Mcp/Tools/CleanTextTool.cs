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
/// MCP tool wrapper around <see cref="ITextCleaningPipeline"/>. Exposes
/// the Layer A / B / C cleaning pipeline as a single MCP tool call so
/// any MCP-compatible agent can strip invisible watermarks from text
/// without shelling out to the CLI.
/// </summary>
[McpServerToolType]
public static class CleanTextTool
{
    [McpServerTool(Name = "clean_text")]
    [Description("Clean plain text through the watermark removal pipeline. Runs Layer A (Unicode hygiene), Layer B (statistical / green-list rewrite, optional), and Layer C (vendor-specific detection).")]
    public static async Task<IEnumerable<ContentBlock>> CleanText(
        ITextCleaningPipeline pipeline,
        AppConfig config,
        [Description("The text payload to clean. May be a snippet, a paragraph, or a multi-kilobyte document.")] string text,
        [Description("When true, enable Layer B (statistical / green-list rewriting). Off by default to keep changes minimal.")] bool? statistical = null,
        [Description("When true, disable Layer A (Unicode hygiene). Off by default; leave on unless you have a reason.")] bool? no_unicode = null,
        [Description("When true, disable Layer C (vendor-specific detectors). Off by default.")] bool? no_vendor = null,
        [Description("When true, include a JSON summary of the removed items and detections alongside the cleaned text. Off by default; pass true to debug or inspect what the pipeline stripped.")] bool include_removed_summary = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(config);

        if (text is null)
        {
            // The MCP SDK surfaces null as a structured tool error rather
            // than an exception — the agent gets a clean message instead
            // of a stack trace.
            throw new McpException("`text` is required and cannot be null.");
        }

        TextCleanOptions options = new()
        {
            EnableUnicode = !(no_unicode ?? false) && config.Text.Layers.Unicode,
            EnableStatistical = (statistical ?? false) || config.Text.Layers.Statistical,
            EnableVendorSpecific = !(no_vendor ?? false) && config.Text.Layers.VendorSpecific,
            LlmEndpoint = config.Text.LlmEndpoint,
            LlmModel = config.Text.LlmModel,
        };

        TextCleanResult result = await pipeline.CleanAsync(text, options, cancellationToken).ConfigureAwait(false);

        // The primary return is always the cleaned text — that's what
        // 99% of agents will use. When the caller explicitly opts in,
        // we append a JSON summary of what was removed and detected.
        List<ContentBlock> blocks = new(capacity: 2)
        {
            new TextContentBlock { Text = result.Cleaned },
        };

        if (include_removed_summary)
        {
            string summary = JsonSerializer.Serialize(new
            {
                removedItems = result.RemovedItems,
                detections = result.Detections,
                confidence = result.Confidence,
            });
            blocks.Add(new TextContentBlock { Text = summary });
        }

        return blocks;
    }
}
