using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for Mistral-style template-leak watermarks.
/// Mistral's chat templates wrap user / system / assistant turns in
/// six literal tokens: <c>[INST]</c>, <c>[/INST]</c>, <c>&lt;&lt;SYS&gt;&gt;</c>,
/// <c>&lt;&lt;/SYS&gt;&gt;</c>, <c>&lt;s&gt;</c>, <c>&lt;/s&gt;</c>. A renderer that
/// skips prompt-sanitisation sometimes lets the markers survive into
/// the user-visible copy. Each marker is a 100% sure signal — the
/// sequences are never natural prose, so a single occurrence is
/// enough to flag the text.
///
/// Like the other vendor detectors, the matches are
/// <i>high-precision, low-recall</i>: we flag the markers but do not
/// try to reverse-engineer any token-level statistical scheme.
/// </summary>
public sealed class MistralWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "Mistral";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // Each token is case-sensitive (the spec says so) and each
        // occurrence is its own match. The Mistral chat template
        // produces the same token multiple times in a single
        // response — one per user / system / assistant turn — so
        // the test suite expects every occurrence to be reported
        // separately.
        foreach (TokenMatch token in FindTokens(text))
        {
            found.Add(new WatermarkMatch(VendorName, "template-leak", 0.99, token.Start, token.Length));
        }

        matches = found;
        return found.Count > 0;
    }

    public string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        // The default behavior is to drop the markers without
        // adding extra whitespace: the user-visible content is what
        // remains, and any whitespace surrounding the marker in
        // the original text is already preserved as part of the
        // pass-through walk. The one refinement: if two markers are
        // immediately adjacent (the end of one equals the start of
        // the next) and removing them would otherwise glue two
        // non-whitespace characters together, insert a single
        // space so the cleaned sentence keeps a word boundary.
        // In practice the [INST] / [/INST] pair is the common case
        // — without a space the cleaned output reads as one word.
        var ranges = new List<(int Start, int End)>(matches.Count);
        foreach (WatermarkMatch m in matches)
        {
            if (m.Pattern == "template-leak")
            {
                ranges.Add((m.Position, m.Position + m.Length));
            }
        }
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        var sb = new StringBuilder(text.Length);
        int rangeIdx = 0;
        for (int i = 0; i < text.Length; i++)
        {
            while (rangeIdx < ranges.Count && i >= ranges[rangeIdx].End)
            {
                rangeIdx++;
            }

            if (rangeIdx < ranges.Count && i >= ranges[rangeIdx].Start)
            {
                // We are entering a marker span. Before we drop it,
                // check whether the chars on either side are both
                // non-whitespace. If they are, splice in a single
                // space so the cleaned sentence keeps a word
                // boundary. The check looks at the *last char we
                // emitted* (so we don't double-space) and the next
                // char outside the marker span.
                char? left = sb.Length > 0 ? sb[sb.Length - 1] : null;
                int afterEnd = ranges[rangeIdx].End;
                char? right = afterEnd < text.Length ? text[afterEnd] : null;
                if (left is char lc && !char.IsWhiteSpace(lc) &&
                    right is char rc && !char.IsWhiteSpace(rc))
                {
                    sb.Append(' ');
                }

                i = ranges[rangeIdx].End - 1; // for-loop's i++ takes us past the marker
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    /// <summary>Walks the text and finds every occurrence of the six
    /// Mistral chat-template markers. All case-sensitive. Returns
    /// (start, length) tuples suitable for direct <see cref="WatermarkMatch"/>
    /// construction.</summary>
    private static List<TokenMatch> FindTokens(ReadOnlySpan<char> text)
    {
        var found = new List<TokenMatch>();
        int i = 0;
        while (i < text.Length)
        {
            int tokenLength = MatchAnyToken(text, i);
            if (tokenLength > 0)
            {
                found.Add(new TokenMatch(i, tokenLength));
                i += tokenLength;
            }
            else
            {
                i++;
            }
        }

        return found;
    }

    /// <summary>Returns the length of the Mistral chat-template
    /// marker starting at <paramref name="offset"/>, or 0 if no
    /// marker starts there. Six tokens total — see the class
    /// summary for the full list.</summary>
    private static int MatchAnyToken(ReadOnlySpan<char> text, int offset)
    {
        if (StartsWithAscii(text, offset, "[INST]"))
        {
            return 6;
        }
        if (StartsWithAscii(text, offset, "[/INST]"))
        {
            return 7;
        }
        if (StartsWithAscii(text, offset, "<<SYS>>"))
        {
            return 7;
        }
        if (StartsWithAscii(text, offset, "<</SYS>>"))
        {
            return 8;
        }
        if (StartsWithAscii(text, offset, "<s>"))
        {
            return 3;
        }
        if (StartsWithAscii(text, offset, "</s>"))
        {
            return 4;
        }
        return 0;
    }

    /// <summary>True if <paramref name="text"/> at offset
    /// <paramref name="offset"/> starts with the literal ASCII
    /// <paramref name="literal"/>. The check is case-sensitive
    /// (the Mistral spec pins the case).</summary>
    private static bool StartsWithAscii(ReadOnlySpan<char> text, int offset, string literal)
    {
        if (offset + literal.Length > text.Length)
        {
            return false;
        }

        for (int j = 0; j < literal.Length; j++)
        {
            if (text[offset + j] != literal[j])
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct TokenMatch(int Start, int Length);
}
