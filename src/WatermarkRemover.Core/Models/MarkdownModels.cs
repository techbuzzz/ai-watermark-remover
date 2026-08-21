namespace WatermarkRemover.Core.Models;

/// <summary>Toggleable options for the markdown cleaner. Defaults follow the specification.</summary>
public record MarkdownCleanOptions
{
    public bool StripHeadings { get; init; } = true;
    public bool StripCodeFences { get; init; }
    public bool StripInlineCode { get; init; }
    public bool StripLinks { get; init; }
    public bool StripImages { get; init; } = true;
    public bool StripBoldItalic { get; init; }
    public bool StripBlockquotes { get; init; }
    public bool StripHr { get; init; } = true;
    public bool StripHtml { get; init; } = true;
    public bool StripComments { get; init; } = true;
    public bool StripTaskLists { get; init; }
    public bool StripTableSyntax { get; init; }
    public bool NormalizeLists { get; init; } = true;
    public bool UnwrapEmptyLists { get; init; } = true;
    public bool StripXmlTags { get; init; } = true;
    public bool StripFrontmatter { get; init; } = true;
    public bool StripAiSignatures { get; init; } = true;
    public bool StripMentions { get; init; } = true;
    public bool StripUnicodeMd { get; init; } = true;
    public bool StripTrailingWs { get; init; } = true;
    public bool ApplyUnicodeLayerA { get; init; } = true;

    /// <summary>Enable every transform (used by <c>--strip-all</c>).</summary>
    public static MarkdownCleanOptions StripAll() => new()
    {
        StripHeadings = true,
        StripCodeFences = true,
        StripInlineCode = true,
        StripLinks = true,
        StripImages = true,
        StripBoldItalic = true,
        StripBlockquotes = true,
        StripHr = true,
        StripHtml = true,
        StripComments = true,
        StripTaskLists = true,
        StripTableSyntax = true,
        NormalizeLists = true,
        UnwrapEmptyLists = true,
        StripXmlTags = true,
        StripFrontmatter = true,
        StripAiSignatures = true,
        StripMentions = true,
        StripUnicodeMd = true,
        StripTrailingWs = true,
        ApplyUnicodeLayerA = true,
    };
}

/// <summary>Result of cleaning a markdown document.</summary>
public record MarkdownCleanResult(
    string Original,
    string Cleaned,
    IReadOnlyList<RemovedItem> RemovedItems,
    IReadOnlyList<AiArtifact> DetectedArtifacts,
    int CodeBlocksFound,
    int CodeBlocksPreserved,
    bool FrontmatterRemoved
);

/// <summary>An AI-specific artifact detected in a markdown document.</summary>
public record AiArtifact(string Type, string Description, int Line, int Column);
