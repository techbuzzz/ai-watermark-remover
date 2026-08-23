using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for DeepSeek-style visible/invisible watermarks.
/// DeepSeek-R1 routinely leaks its <c><think>…</think></c> reasoning
/// trace into the final answer (the chain-of-thought is meant to be
/// stripped by the serving stack, but the user-visible copy often
/// still carries the tags), and DeepSeek's CJK-heavy training data
/// causes fullwidth ASCII punctuation to leak into otherwise-Latin
/// passages. We flag both.
///
/// The detector is <i>high-precision, low-recall</i> by design: the
/// <c><think></c> tag is a near-perfect signal, and a single
/// fullwidth code point in an otherwise-Latin sentence is suspicious
/// enough to surface. We do not try to verify the
/// green-list/red-list token-level statistical watermark that the
/// DeepSeek papers describe — that requires the secret key.
/// </summary>
public sealed class DeepSeekWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "DeepSeek";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // 1. <think> and </think> tags (case-insensitive — DeepSeek-R1
        // has shipped both forms in different snapshots, and downstream
        // renderers sometimes lower-case the prompt template).
        foreach (TagMatch tag in FindThinkTags(text))
        {
            found.Add(new WatermarkMatch(VendorName, "reasoning-block", 0.95, tag.Start, tag.Length));
        }

        // 2. Fullwidth ASCII code points (U+FF01..U+FF5E) appearing
        // adjacent to ASCII Latin letters. A fullwidth comma in the
        // middle of an English sentence is the cheapest possible
        // DeepSeek fingerprint; in genuine CJK prose the neighbours
        // are also CJK, so the boundary condition keeps us out of
        // false positives on Chinese text.
        for (int i = 0; i < text.Length; i++)
        {
            if (IsFullwidthAscii(text[i]) && HasAsciiLetterNeighbour(text, i))
            {
                found.Add(new WatermarkMatch(VendorName, "fullwidth-punctuation", 0.7, i, 1));
            }
        }

        matches = found;
        return found.Count > 0;
    }

    public string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        // Walk the text. For each character:
        //  * If it's inside a <think> / </think> tag span, drop it.
        //  * If it's a fullwidth ASCII code point bordered by an ASCII
        //    Latin letter (i.e. the same predicate the detector uses),
        //    fold it to its ASCII twin.
        //  * Otherwise, copy through unchanged.
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (IsInsideThinkTag(text, i))
            {
                continue;
            }

            char c = text[i];
            if (IsFullwidthAscii(c) && HasAsciiLetterNeighbour(text, i))
            {
                sb.Append(MapFullwidthToAscii(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>True for the U+FF01..U+FF5E fullwidth ASCII twin block.</summary>
    private static bool IsFullwidthAscii(char c) => c >= '\uFF01' && c <= '\uFF5E';

    /// <summary>True for an ASCII Latin letter (a-z / A-Z).</summary>
    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    /// <summary>True if the position is bordered by an ASCII letter
    /// (either side) — that's the "Latin passage" gate that keeps
    /// genuine CJK prose from being flagged.</summary>
    private static bool HasAsciiLetterNeighbour(ReadOnlySpan<char> text, int i)
    {
        bool left = i > 0 && IsAsciiLetter(text[i - 1]);
        bool right = i + 1 < text.Length && IsAsciiLetter(text[i + 1]);
        return left || right;
    }

    /// <summary>Map a fullwidth ASCII code point to its ASCII twin
    /// (U+FF01 → U+0021 … U+FF5E → U+007E). Returns the input
    /// unchanged when it's outside the range.</summary>
    private static char MapFullwidthToAscii(char c) =>
        IsFullwidthAscii(c) ? (char)(c - 0xFF01 + 0x0021) : c;

    /// <summary>True when <paramref name="position"/> falls inside
    /// a <c>&lt;think…&gt;</c> or <c>&lt;/think…&gt;</c> tag
    /// (case-insensitive). Used by <see cref="Remove"/> to drop
    /// the tag characters while keeping the user-visible
    /// reasoning content that may sit between an open and a
    /// close tag.</summary>
    private static bool IsInsideThinkTag(ReadOnlySpan<char> text, int position)
    {
        // Walk backwards from `position` to the previous '<' (if any).
        // The smallest valid <think> / </think> tag is 7 characters
        // (e.g. "<think>"), so a backward scan up to 8 chars catches
        // both the bare "<think" form and the "<think>" form.
        int scan = position;
        int limit = Math.Max(0, position - 8);
        while (scan > limit)
        {
            char c = text[scan - 1];
            if (c == '<')
            {
                // Build the substring from '<' through the previous
                // char before `position` and see if it starts a tag.
                ReadOnlySpan<char> candidate = text.Slice(scan - 1, position - (scan - 1));
                return candidate.StartsWith("<think", StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith("</think", StringComparison.OrdinalIgnoreCase);
            }

            // A non-letter, non-'>', non-'/' character inside the
            // candidate means it's not part of a think-tag.
            if (c != '>' && c != '/' && !IsAsciiLetter(c))
            {
                return false;
            }

            scan--;
        }

        return false;
    }

    /// <summary>Finds every <c>&lt;think</c> and <c>&lt;/think</c>
    /// tag in the text, case-insensitive. Returns the (start, length)
    /// of the opening angle bracket through the first non-tag
    /// character — that's the minimum span that, when removed, takes
    /// the tag with it.</summary>
    private static List<TagMatch> FindThinkTags(ReadOnlySpan<char> text)
    {
        var found = new List<TagMatch>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                int tagStart = i;
                int scan = i + 1;
                if (scan < text.Length && text[scan] == '/')
                {
                    scan++;
                }

                if (MatchesAsciiWord(text, scan, "think", caseInsensitive: true))
                {
                    int after = scan + "think".Length;
                    // Accept an optional '>' or ' ' (DeepSeek-R1 sometimes
                    // emits "<think>" with a closing angle, sometimes just
                    // "<think" as a sentinel without one).
                    int tagEnd = after;
                    if (tagEnd < text.Length && text[tagEnd] == '>')
                    {
                        tagEnd++;
                    }

                    found.Add(new TagMatch(tagStart, tagEnd - tagStart));
                    i = tagEnd;
                    continue;
                }
            }

            i++;
        }

        return found;
    }

    /// <summary>True if <paramref name="text"/> at offset
    /// <paramref name="offset"/> matches <paramref name="word"/>
    /// character-for-character (optionally case-insensitive).</summary>
    private static bool MatchesAsciiWord(ReadOnlySpan<char> text, int offset, string word, bool caseInsensitive)
    {
        if (offset + word.Length > text.Length)
        {
            return false;
        }

        for (int j = 0; j < word.Length; j++)
        {
            char a = text[offset + j];
            char b = word[j];
            if (caseInsensitive)
            {
                if (char.ToLowerInvariant(a) != char.ToLowerInvariant(b))
                {
                    return false;
                }
            }
            else if (a != b)
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct TagMatch(int Start, int Length);
}
