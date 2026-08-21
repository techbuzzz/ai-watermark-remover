namespace WatermarkRemover.Text.Vendors;

/// <summary>Shared helpers for vendor-specific invisible-watermark heuristics.</summary>
internal static class VendorPatterns
{
    /// <summary>Zero-width / invisible code points commonly abused for steganography.</summary>
    internal static readonly HashSet<char> ZeroWidth =
    [
        '\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF',
        '\u2061', '\u2062', '\u2063', '\u2064',
    ];

    /// <summary>Bidirectional override / isolate controls.</summary>
    internal static readonly HashSet<char> BidiControls =
    [
        '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
        '\u2066', '\u2067', '\u2068', '\u2069',
    ];

    /// <summary>Variation selectors (VS1–VS16) — used for character-level steganography.</summary>
    internal static bool IsVariationSelector(char c) => c is >= '\uFE00' and <= '\uFE0F';

    /// <summary>
    /// Homoglyph map: non-Latin look-alike → canonical ASCII letter.
    /// Presence of these inside otherwise-Latin words is a strong steganography signal.
    /// </summary>
    internal static readonly Dictionary<char, char> Homoglyphs = new()
    {
        // Cyrillic look-alikes
        ['\u0410'] = 'A', ['\u0412'] = 'B', ['\u0415'] = 'E', ['\u041A'] = 'K',
        ['\u041C'] = 'M', ['\u041D'] = 'H', ['\u041E'] = 'O', ['\u0420'] = 'P',
        ['\u0421'] = 'C', ['\u0422'] = 'T', ['\u0425'] = 'X', ['\u0430'] = 'a',
        ['\u0435'] = 'e', ['\u043E'] = 'o', ['\u0440'] = 'p', ['\u0441'] = 'c',
        ['\u0443'] = 'y', ['\u0445'] = 'x', ['\u0455'] = 's', ['\u0456'] = 'i',
        ['\u0458'] = 'j',
        // Greek look-alikes
        ['\u0391'] = 'A', ['\u0392'] = 'B', ['\u0395'] = 'E', ['\u0396'] = 'Z',
        ['\u0397'] = 'H', ['\u0399'] = 'I', ['\u039A'] = 'K', ['\u039C'] = 'M',
        ['\u039D'] = 'N', ['\u039F'] = 'O', ['\u03A1'] = 'P', ['\u03A4'] = 'T',
        ['\u03A5'] = 'Y', ['\u03A7'] = 'X', ['\u03BF'] = 'o', ['\u03B1'] = 'a',
    };

    /// <summary>Find contiguous runs where <paramref name="predicate"/> holds. Returns (start, length) pairs.</summary>
    internal static List<(int Start, int Length)> FindRuns(ReadOnlySpan<char> text, Func<char, bool> predicate, int minRun = 1)
    {
        var runs = new List<(int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            if (predicate(text[i]))
            {
                int start = i;
                while (i < text.Length && predicate(text[i]))
                {
                    i++;
                }

                int len = i - start;
                if (len >= minRun)
                {
                    runs.Add((start, len));
                }
            }
            else
            {
                i++;
            }
        }

        return runs;
    }
}
