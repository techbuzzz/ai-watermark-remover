using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Core.Interfaces;

/// <summary>Layer A — strips invisible / normalizing Unicode code points.</summary>
public interface IUnicodeHygieneCleaner
{
    TextCleanResult Clean(string input);
}

/// <summary>Layer B — statistical (green-list) watermark rewriter.</summary>
public interface IStatisticalWatermarkRewriter
{
    Task<TextCleanResult> RewriteAsync(string input, TextCleanOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Layer C — a vendor-specific invisible watermark detector plugin.</summary>
public interface IAiTextWatermarkDetector
{
    string VendorName { get; }
    bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches);
    string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches);
}

/// <summary>Orchestrates Layers A, B and C into a single cleaning operation.</summary>
public interface ITextCleaningPipeline
{
    Task<TextCleanResult> CleanAsync(string text, TextCleanOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Detect (without removing) all watermark signatures in the given text.</summary>
    IReadOnlyList<WatermarkMatch> Detect(string text);
}

/// <summary>Cleans markdown documents while preserving code blocks.</summary>
public interface IMarkdownCleaner
{
    MarkdownCleanResult Clean(string markdown, MarkdownCleanOptions? options = null);

    /// <summary>Detect (without removing) AI artifacts in a markdown document.</summary>
    IReadOnlyList<AiArtifact> Detect(string markdown);
}
