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
/// MCP tool wrapper around <see cref="IMarkdownCleaner"/>. Strips
/// AI-specific artifacts (frontmatter, signatures, mentions, code-block
/// decorations) from a markdown document while preserving real code
/// fences and their content.
/// </summary>
[McpServerToolType]
public static class CleanMarkdownTool
{
    [McpServerTool(Name = "clean_markdown")]
    [Description("Clean a markdown document. Strips frontmatter, AI signatures, mentions, link / image watermarks, and (optionally) headings, list markers, and more. Always preserves fenced code blocks unless --strip_code_fences is set.")]
    public static IEnumerable<ContentBlock> CleanMarkdown(
        IMarkdownCleaner cleaner,
        AppConfig config,
        [Description("The markdown document to clean. Fenced code blocks are preserved.")] string markdown,
        [Description("When true, also strip code fences and their content. Off by default; the cleaner only edits prose by default.")] bool strip_code_fences = false,
        [Description("When true, also strip headings (`#`, `##`, …). Off by default.")] bool strip_headings = false,
        [Description("When true, also strip links / images. Off by default.")] bool strip_links = false,
        [Description("When true, include a JSON summary of removed items alongside the cleaned markdown.")] bool include_removed_summary = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        ArgumentNullException.ThrowIfNull(config);

        if (markdown is null)
        {
            throw new McpException("`markdown` is required and cannot be null.");
        }

        MarkdownCleanOptions options = MarkdownCleanOptions.From(config.Markdown);
        // CLI flags are per-call overrides layered on top of the config baseline.
        MarkdownCleanOptions overrides = options with
        {
            StripCodeFences = strip_code_fences || options.StripCodeFences,
            StripHeadings = strip_headings || options.StripHeadings,
            StripLinks = strip_links || options.StripLinks,
        };

        MarkdownCleanResult result = cleaner.Clean(markdown, overrides);

        List<ContentBlock> blocks = new(capacity: 2)
        {
            new TextContentBlock { Text = result.Cleaned },
        };

        if (include_removed_summary)
        {
            string summary = JsonSerializer.Serialize(new
            {
                removedItems = result.RemovedItems,
                detectedArtifacts = result.DetectedArtifacts,
                codeBlocksFound = result.CodeBlocksFound,
                codeBlocksPreserved = result.CodeBlocksPreserved,
                frontmatterRemoved = result.FrontmatterRemoved,
            });
            blocks.Add(new TextContentBlock { Text = summary });
        }

        return blocks;
    }
}
