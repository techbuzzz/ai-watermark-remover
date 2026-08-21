using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text;

/// <summary>Orchestrates Layer A (Unicode), Layer C (vendor) and Layer B (statistical) cleaning.</summary>
public sealed class TextCleaningPipeline(
    IUnicodeHygieneCleaner unicodeCleaner,
    IStatisticalWatermarkRewriter statisticalRewriter,
    IEnumerable<IAiTextWatermarkDetector> vendorDetectors) : ITextCleaningPipeline
{
    private readonly IUnicodeHygieneCleaner _unicodeCleaner = unicodeCleaner;
    private readonly IStatisticalWatermarkRewriter _statisticalRewriter = statisticalRewriter;
    private readonly IReadOnlyList<IAiTextWatermarkDetector> _vendorDetectors = vendorDetectors.ToList();

    /// <inheritdoc />
    public async Task<TextCleanResult> CleanAsync(string text, TextCleanOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new TextCleanOptions();

        if (text.Length == 0)
        {
            return TextCleanResult.Empty;
        }

        string working = text;
        var removed = new List<RemovedItem>();
        var detections = new List<WatermarkMatch>();
        double confidence = 0.0;

        // Layer A — Unicode hygiene.
        if (options.EnableUnicode)
        {
            TextCleanResult unicodeResult = _unicodeCleaner.Clean(working);
            working = unicodeResult.Cleaned;
            removed.AddRange(unicodeResult.RemovedItems);
            confidence = Math.Max(confidence, unicodeResult.Confidence);
        }

        // Layer C — vendor-specific detection & removal.
        if (options.EnableVendorSpecific)
        {
            foreach (IAiTextWatermarkDetector detector in _vendorDetectors)
            {
                if (detector.Detect(working, out IReadOnlyList<WatermarkMatch> matches) && matches.Count > 0)
                {
                    detections.AddRange(matches);
                    string before = working;
                    working = detector.Remove(before, matches);
                    if (!string.Equals(before, working, StringComparison.Ordinal))
                    {
                        removed.Add(new RemovedItem("vendor-watermark", 0, 0, $"{detector.VendorName}: removed {matches.Count} watermark signal(s)"));
                    }

                    confidence = Math.Max(confidence, matches.Max(m => m.Confidence));
                }
            }
        }

        // Layer B — statistical rewrite.
        if (options.EnableStatistical)
        {
            TextCleanResult statResult = await _statisticalRewriter.RewriteAsync(working, options, cancellationToken).ConfigureAwait(false);
            working = statResult.Cleaned;
            removed.AddRange(statResult.RemovedItems);
            confidence = Math.Max(confidence, statResult.Confidence);
        }

        return new TextCleanResult(text, working, removed, detections, confidence);
    }

    /// <inheritdoc />
    public IReadOnlyList<WatermarkMatch> Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var detections = new List<WatermarkMatch>();
        foreach (IAiTextWatermarkDetector detector in _vendorDetectors)
        {
            if (detector.Detect(text, out IReadOnlyList<WatermarkMatch> matches))
            {
                detections.AddRange(matches);
            }
        }

        return detections;
    }
}
