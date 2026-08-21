using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for OpenAI style watermarks. OpenAI's scheme is a token-level
/// statistical watermark; as a practical heuristic we flag invisible carriers (zero-width
/// joiners, word joiners, BOM mid-stream) and bidirectional overrides that can smuggle bits.
/// </summary>
public sealed class OpenAiWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "OpenAI";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // BOM / word-joiner appearing mid-stream (not at position 0) is suspicious.
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] is '\uFEFF' or '\u2060')
            {
                found.Add(new WatermarkMatch(VendorName, "mid-stream-joiner", 0.7, i, 1));
            }
        }

        // Bidirectional overrides used to reorder/hide content.
        foreach (var (start, len) in VendorPatterns.FindRuns(text, c => VendorPatterns.BidiControls.Contains(c), minRun: 1))
        {
            found.Add(new WatermarkMatch(VendorName, "bidi-override", 0.75, start, len));
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
            if (c is '\uFEFF' or '\u2060' || VendorPatterns.BidiControls.Contains(c))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
