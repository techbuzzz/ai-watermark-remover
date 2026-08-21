using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for Google Gemini / SynthID-Text style watermarks.
/// SynthID-Text is a token-level statistical scheme; without the secret key we cannot
/// verify it, so we flag its typical carriers: variation selectors and isolated
/// zero-width markers inserted at word boundaries.
/// </summary>
public sealed class GeminiWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "Gemini";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // Variation selectors used as invisible per-token markers.
        foreach (var (start, len) in VendorPatterns.FindRuns(text, VendorPatterns.IsVariationSelector, minRun: 1))
        {
            found.Add(new WatermarkMatch(VendorName, "variation-selector", 0.8, start, len));
        }

        // Isolated zero-width markers at word boundaries (SynthID token boundary carriers).
        for (int i = 0; i < text.Length; i++)
        {
            if (VendorPatterns.ZeroWidth.Contains(text[i]))
            {
                bool boundary =
                    (i == 0 || char.IsWhiteSpace(text[i - 1]) || char.IsLetterOrDigit(text[i - 1])) &&
                    (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]) || char.IsLetterOrDigit(text[i + 1]));
                if (boundary)
                {
                    found.Add(new WatermarkMatch(VendorName, "synthid-token-marker", 0.6, i, 1));
                }
            }
        }

        matches = found;
        return found.Count > 0;
    }

    public string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (VendorPatterns.IsVariationSelector(c) || VendorPatterns.ZeroWidth.Contains(c))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
