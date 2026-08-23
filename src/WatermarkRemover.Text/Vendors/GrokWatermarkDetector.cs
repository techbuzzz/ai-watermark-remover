using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Vendors;

/// <summary>
/// Best-effort detector for xAI Grok-style stylistic fingerprints.
/// Grok's <c>grok-2</c> persona injects 3–5 emoji in a row at the
/// start of a response, and its prose over-uses em-dashes in a way
/// Claude / Gemini / OpenAI / DeepSeek do not. We flag both.
///
/// Like the other vendor detectors, the matches are
/// <i>high-precision, low-recall</i>: a single emoji isn't suspicious
/// (humans use emoji too), but a 3+ emoji burst at the start of a
/// paragraph is a Grok signature. The detector does not try to
/// reverse-engineer the green-list / red-list statistical watermark
/// that the xAI research team has hinted at — that requires the
/// secret key.
/// </summary>
public sealed class GrokWatermarkDetector : IAiTextWatermarkDetector
{
    public string VendorName => "Grok";

    public bool Detect(ReadOnlySpan<char> text, out IReadOnlyList<WatermarkMatch> matches)
    {
        var found = new List<WatermarkMatch>();

        // 1. Emoji bursts — 3+ consecutive emoji code points. We
        // walk the runs we already collected (FindEmojiRuns returns
        // them with minRun=3) and flag each as a single match.
        foreach (EmojiRun run in FindEmojiRuns(text, minRun: 3))
        {
            found.Add(new WatermarkMatch(VendorName, "emoji-burst", 0.6, run.Start, run.Length));
        }

        // 2. Em-dash clusters — 3+ consecutive U+2014 em-dashes.
        // The existing VendorPatterns.FindRuns helper does exactly
        // the contiguous-run scan we need; we just feed it a
        // lambda and the minRun threshold.
        foreach (var (start, length) in VendorPatterns.FindRuns(text, c => c == '\u2014', minRun: 3))
        {
            found.Add(new WatermarkMatch(VendorName, "em-dash-cluster", 0.7, start, length));
        }

        matches = found;
        return found.Count > 0;
    }

    public string Remove(ReadOnlySpan<char> text, IReadOnlyList<WatermarkMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var sb = new StringBuilder(text.Length);

        // Walk the text, collapsing emoji bursts to a single emoji
        // and em-dash clusters to a single em-dash. This keeps the
        // document readable rather than deleting the fingerprint
        // outright, matching the "soft normalisation" posture the
        // MarkdownCleaner uses for its emoji-driven sign-off toggle.
        for (int i = 0; i < text.Length; i++)
        {
            if (IsEmojiCodePoint(text[i]))
            {
                // Look ahead to find the end of the emoji run, then
                // emit one emoji + skip the rest. We step the
                // surrogate-pair "char" as a single unit: a high
                // surrogate (U+D800..U+DBFF) is consumed together
                // with the following low surrogate so the run
                // doesn't double-count a single visual emoji.
                int j = i;
                while (j < text.Length && IsEmojiCodePoint(text[j]))
                {
                    if (IsHighSurrogate(text[j]) && j + 1 < text.Length)
                    {
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                // Emit the first emoji of the run. If it's a
                // surrogate pair, emit both halves; otherwise emit
                // the single BMP char.
                if (IsHighSurrogate(text[i]) && i + 1 < text.Length)
                {
                    sb.Append(text[i]);
                    sb.Append(text[i + 1]);
                    i = j - 1; // outer for-loop's i++ takes us past the run
                }
                else
                {
                    sb.Append(text[i]);
                    i = j - 1;
                }
                continue;
            }

            if (text[i] == '\u2014')
            {
                int j = i;
                while (j < text.Length && text[j] == '\u2014')
                {
                    j++;
                }

                sb.Append('\u2014');
                i = j - 1;
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    /// <summary>True for an emoji code point. The detection covers
    /// the BMP blocks that hold most emoji (Misc Symbols,
    /// Misc Symbols &amp; Pictographs, Emoticons, Transport &amp;
    /// Map), the variation-selector trailer <c>U+FE0F</c>, and the
    /// high / low surrogate range (U+D800..U+DFFF) so the
    /// supplementary-plane emoji (the popular 😀..🚀 range
    /// U+1F300..U+1FAFF, regional indicators U+1F1E6..U+1F1FF, and
    /// everything else above the BMP) is matched too. Not an
    /// exhaustive emoji list — just enough to catch Grok's bursts
    /// without firing on basic punctuation.</summary>
    private static bool IsEmojiCodePoint(char c) =>
        c is '\u2600' or '\u2601' or '\u2602' or '\u2603' or '\u2604' or '\u2605'
          or '\u260E' or '\u2611' or '\u2614' or '\u2615' or '\u2618' or '\u261D'
          or '\u2620' or '\u2622' or '\u2623' or '\u2626' or '\u262A' or '\u262E' or '\u262F'
          or '\u2638' or '\u2639' or '\u263A' or '\u2640' or '\u2642' or '\u2648' or '\u2649'
          or '\u2654' or '\u2660' or '\u2663' or '\u2665' or '\u2666' or '\u2668'
          or '\u267B' or '\u267E' or '\u267F' or '\u2692' or '\u2694' or '\u2696' or '\u2697'
          or '\u2699' or '\u269B' or '\u269C' or '\u26A0' or '\u26A1' or '\u26AA' or '\u26AB'
          or '\u26B0' or '\u26B1' or '\u26BD' or '\u26BE' or '\u26C4' or '\u26C5' or '\u26C8'
          or '\u26CE' or '\u26CF' or '\u26D1' or '\u26D3' or '\u26D4' or '\u26E9' or '\u26EA'
          or '\u26F0' or '\u26F1' or '\u26F2' or '\u26F3' or '\u26F4' or '\u26F5' or '\u26F7' or '\u26F8' or '\u26F9' or '\u26FA'
          or '\u2702' or '\u2705' or '\u2708' or '\u2709' or '\u270A' or '\u270B' or '\u270C' or '\u270D' or '\u270E' or '\u270F'
          or '\u2712' or '\u2714' or '\u2716' or '\u271D' or '\u2721' or '\u2728' or '\u2733' or '\u2734' or '\u2744' or '\u2747' or '\u274C' or '\u274E'
          or '\u2753' or '\u2754' or '\u2755' or '\u2757' or '\u2763' or '\u2764' or '\u2795' or '\u2796' or '\u2797' or '\u27A1' or '\u27B0'
          or '\u27BF' or '\u2934' or '\u2935' or '\u2B05' or '\u2B06' or '\u2B07' or '\u2B1B' or '\u2B1C' or '\u2B50' or '\u2B55'
          or '\u3030' or '\u303D' or '\u3297' or '\u3299' or '\uFE0F'
          || (c >= '\uD800' && c <= '\uDFFF');

    /// <summary>True for a UTF-16 high surrogate (D800..DBFF).
    /// High surrogates always come first in a surrogate pair that
    /// encodes a supplementary-plane code point.</summary>
    private static bool IsHighSurrogate(char c) => c is >= '\uD800' and <= '\uDBFF';

    /// <summary>Walks the text and returns contiguous emoji runs
    /// of at least <paramref name="minRun"/> code points. Emojis
    /// with intervening zero-width joiners (U+200D) are treated
    /// as part of the same run — the existing Claude detector's
    /// <c>zero-width-sequence</c> pattern catches that.</summary>
    private static List<EmojiRun> FindEmojiRuns(ReadOnlySpan<char> text, int minRun)
    {
        var runs = new List<EmojiRun>();
        int i = 0;
        while (i < text.Length)
        {
            if (IsEmojiCodePoint(text[i]))
            {
                int start = i;
                while (i < text.Length && (IsEmojiCodePoint(text[i]) || text[i] == '\u200D'))
                {
                    // Step over a surrogate pair as a single unit
                    // so the run length (in chars) doesn't
                    // double-count a supplementary-plane emoji.
                    if (IsHighSurrogate(text[i]) && i + 1 < text.Length)
                    {
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                int len = i - start;
                if (len >= minRun)
                {
                    runs.Add(new EmojiRun(start, len));
                }
            }
            else
            {
                i++;
            }
        }

        return runs;
    }

    private readonly record struct EmojiRun(int Start, int Length);
}
