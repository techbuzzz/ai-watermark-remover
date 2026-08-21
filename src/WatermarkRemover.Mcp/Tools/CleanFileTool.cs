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
/// MCP tool wrapper around <see cref="IFileCleanerRouter"/>. Strips
/// metadata (EXIF, XMP, IPTC, C2PA, etc.) from a single file on disk
/// and returns the cleaned bytes as a base64-encoded resource block so
/// the agent can hand the result back to its own caller or write it
/// elsewhere.
/// </summary>
[McpServerToolType]
public static class CleanFileTool
{
    [McpServerTool(Name = "clean_file")]
    [Description("Strip metadata (EXIF, XMP, IPTC, C2PA, and others) from a single file. Supports JPEG, PNG, WebP, PDF, DOCX, and HTML. Writes the cleaned copy to a temp path and returns the bytes as a base64-encoded resource block alongside a JSON summary of what was removed.")]
    public static async Task<IEnumerable<ContentBlock>> CleanFile(
        IFileCleanerRouter router,
        AppConfig config,
        [Description("Absolute path to the file to clean. The file must exist and be on a supported format (JPEG / PNG / WebP / PDF / DOCX / HTML).")] string input_path,
        [Description("Optional override directory for the cleaned output. When omitted, the cleaned file is written next to the input with a `.cleaned` suffix.")] string? output_directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(config);

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

        string outputPath = ResolveOutputPath(input_path, output_directory);

        MetadataCleanOptions options = new()
        {
            StripExif = config.Metadata.StripExif,
            StripXmp = config.Metadata.StripXmp,
            StripC2pa = config.Metadata.StripC2pa,
            PreserveColorProfile = config.Metadata.PreserveColorProfile,
        };

        FileCleanResult result = router.Clean(input_path, outputPath, options);
        byte[] cleanedBytes = await File.ReadAllBytesAsync(result.OutputPath, cancellationToken).ConfigureAwait(false);
        string mime = GuessMimeType(result.OutputPath);
        string fileUri = $"file://{result.OutputPath.Replace('\\', '/')}";

        // The summary is the agent-friendly sidecar; the resource block
        // is the actual deliverable (cleaned bytes), which the agent can
        // write back to disk, send over the wire, or embed in a UI.
        string summary = JsonSerializer.Serialize(new
        {
            inputPath = result.InputPath,
            outputPath = result.OutputPath,
            removedEntries = result.RemovedEntries,
            inputSizeBytes = result.InputSizeBytes,
            outputSizeBytes = result.OutputSizeBytes,
            processingTimeMs = result.ProcessingTime.TotalMilliseconds,
            mimeType = mime,
        });

        return new ContentBlock[]
        {
            new TextContentBlock { Text = summary },
            new EmbeddedResourceBlock
            {
                Resource = BlobResourceContents.FromBytes(cleanedBytes, fileUri, mime),
            },
        };
    }

    private static string ResolveOutputPath(string inputPath, string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            string dir = Path.GetDirectoryName(inputPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(inputPath);
            string ext = Path.GetExtension(inputPath);
            return Path.Combine(dir, $"{name}.cleaned{ext}");
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        return Path.Combine(outputDirectory, Path.GetFileName(inputPath));
    }

    private static string GuessMimeType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".html" or ".htm" => "text/html",
            _ => "application/octet-stream",
        };
    }
}
