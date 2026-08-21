namespace WatermarkRemover.Core.Models;

/// <summary>Result of cleaning a plain-text payload through the text pipeline.</summary>
public record TextCleanResult(
    string Original,
    string Cleaned,
    IReadOnlyList<RemovedItem> RemovedItems,
    IReadOnlyList<WatermarkMatch> Detections,
    double Confidence
)
{
    /// <summary>Convenience empty result for empty input.</summary>
    public static TextCleanResult Empty { get; } =
        new(string.Empty, string.Empty, [], [], 0.0);
}

/// <summary>A single item removed from a payload during cleaning.</summary>
public record RemovedItem(string Type, int Position, int Length, string Description);

/// <summary>A detected watermark signature match.</summary>
public record WatermarkMatch(string Vendor, string Pattern, double Confidence, int Position, int Length);
