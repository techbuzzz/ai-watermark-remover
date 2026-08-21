using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for Anthropic Claude style invisible watermarks.
/// Heuristics: homoglyph substitution inside Latin words, and specific zero-width
/// character sequences (ZWSP/ZWJ combinations) embedded between visible characters.
/// </summary>
public sealed class ClaudeWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "Claude";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // 1. Homoglyph substitution: non-Latin look-alikes surrounded by ASCII letters.
        for (int i = 0; i < text.Length; i++)
        {
            if (VendorPatterns.Homoglyphs.ContainsKey(text[i]))
            {
                bool neighborLatin =
                    (i > 0 && IsAsciiLetter(text[i - 1])) ||
                    (i + 1 < text.Length && IsAsciiLetter(text[i + 1]));
                if (neighborLatin)
                {
                    found.Add(new WatermarkMatch(VendorName, "homoglyph-substitution", 0.85, i, 1));
                }
            }
        }

        // 2. Zero-width runs of length >= 2 (steganographic bit-encoding signature).
        foreach (var (start, len) in VendorPatterns.FindRuns(text, c => VendorPatterns.ZeroWidth.Contains(c), minRun: 2))
        {
            found.Add(new WatermarkMatch(VendorName, "zero-width-sequence", 0.9, start, len));
        }

        matches = found;
        return found.Count > 0;
    }

    public string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (VendorPatterns.ZeroWidth.Contains(c))
            {
                continue; // drop zero-width steganography carriers
            }

            if (VendorPatterns.Homoglyphs.TryGetValue(c, out char canonical))
            {
                bool neighborLatin =
                    (i > 0 && IsAsciiLetter(text[i - 1])) ||
                    (i + 1 < text.Length && IsAsciiLetter(text[i + 1]));
                sb.Append(neighborLatin ? canonical : c);
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
