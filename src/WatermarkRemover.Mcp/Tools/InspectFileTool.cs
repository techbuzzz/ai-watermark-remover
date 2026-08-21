using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Mcp.Tools;

/// <summary>
/// MCP tool wrapper around <see cref="IFileCleanerRouter.Inspect"/>.
/// Reports every metadata entry (EXIF tags, XMP properties, C2PA
/// claims, etc.) found in a file without modifying it. Useful for
/// "what's actually in this file?" workflows.
/// </summary>
[McpServerToolType]
public static class InspectFileTool
{
    [McpServerTool(Name = "inspect_file")]
    [Description("Inspect (without removing) every metadata entry in a single file. Returns the full list of `MetadataEntry` records with container, key, and value. Use this before `clean_file` if you want to confirm what is about to be stripped.")]
    public static TextContentBlock InspectFile(
        IFileCleanerRouter router,
        [Description("Absolute path to the file to inspect. The file must exist and be on a supported format.")] string input_path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);

        if (string.IsNullOrWhiteSpace(input_path))
        {
            throw new McpException("`input_path` is required and cannot be empty.");
        }

        if (!File.Exists(input_path))
        {
            throw new McpException($"File not found: {input_path}");
        }

        if (!router.IsSupported(input_path))
        {
            throw new McpException($"Unsupported file type: {Path.GetExtension(input_path)}");
        }

        IReadOnlyList<MetadataEntry> entries = router.Inspect(input_path);
        string json = JsonSerializer.Serialize(entries);
        return new TextContentBlock { Text = json };
    }
}
