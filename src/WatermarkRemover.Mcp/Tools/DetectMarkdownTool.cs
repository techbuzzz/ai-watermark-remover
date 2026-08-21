using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Mcp.Tools;

/// <summary>
/// MCP tool wrapper around <see cref="IMarkdownCleaner.Detect"/>.
/// Reports AI artifacts (frontmatter, signatures, etc.) found in a
/// markdown document without modifying it.
/// </summary>
[McpServerToolType]
public static class DetectMarkdownTool
{
    [McpServerTool(Name = "detect_markdown")]
    [Description("Detect (without removing) AI artifacts in a markdown document: frontmatter, vendor signatures, mentions, and similar patterns. Returns an array of `AiArtifact` records with type, description, line, and column.")]
    public static TextContentBlock DetectMarkdown(
        IMarkdownCleaner cleaner,
        [Description("The markdown document to inspect.")] string markdown,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cleaner);

        if (markdown is null)
        {
            throw new McpException("`markdown` is required and cannot be null.");
        }

        IReadOnlyList<AiArtifact> artifacts = cleaner.Detect(markdown);
        string json = JsonSerializer.Serialize(artifacts);
        return new TextContentBlock { Text = json };
    }
}
