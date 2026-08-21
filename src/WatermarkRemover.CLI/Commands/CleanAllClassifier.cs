using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.CLI.Commands;

/// <summary>
/// Routes a file path to the right cleaning pipeline for the <c>clean-all</c>
/// command. Decision is purely extension + router lookup, with a hard
/// "unsupported" verdict for binary files that the router doesn't know
/// and that aren't obviously text — those are skipped with a warning
/// rather than fed to the text pipeline by mistake.
/// </summary>
public static class CleanAllClassifier
{
    /// <summary>Which pipeline should handle a file.</summary>
    public enum Pipeline
    {
        /// <summary>Strip metadata via <see cref="IFileCleanerRouter"/> (images, documents, …).</summary>
        FileMetadata,

        /// <summary>Markdown cleaner (preserves code blocks).</summary>
        Markdown,

        /// <summary>Plain-text pipeline (Layers A/B/C).</summary>
        Text,

        /// <summary>Unknown binary — skip, do not fall back to the text pipeline.</summary>
        Unsupported,
    }

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".text",
        ".log",
    };

    /// <summary>
    /// Classify a file path. Markdown wins over router when both match (we
    /// never want metadata stripping on a <c>.md</c>). For files without
    /// an extension we conservatively route to the text pipeline.
    /// </summary>
    public static Pipeline Classify(string path, IFileCleanerRouter router)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(router);

        string ext = Path.GetExtension(path);

        if (MarkdownExtensions.Contains(ext))
        {
            return Pipeline.Markdown;
        }

        if (router.IsSupported(path))
        {
            return Pipeline.FileMetadata;
        }

        if (TextExtensions.Contains(ext) || string.IsNullOrEmpty(ext))
        {
            return Pipeline.Text;
        }

        return Pipeline.Unsupported;
    }
}
