using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text;

/// <summary>
/// Layer A — Unicode hygiene. Strips invisible / formatting code points, applies NFKC
/// normalization, and normalizes smart quotes, dashes, ellipses and non-breaking spaces.
/// </summary>
public sealed class UnicodeHygieneCleaner : IUnicodeHygieneCleaner
{
    /// <summary>Single code points that are removed outright.</summary>
    private static readonly HashSet<int> StrippedCodePoints =
    [
        0x200B, // Zero-Width Space
        0x200C, // Zero-Width Non-Joiner
        0x200D, // Zero-Width Joiner
        0x200E, // Left-to-Right Mark
        0x200F, // Right-to-Left Mark
        0x2060, // Word Joiner
        0x2061, // Function Application
        0x2062, // Invisible Times
        0x2063, // Invisible Separator
        0x2064, // Invisible Plus
        0xFEFF, // Zero-Width No-Break Space / BOM
        0x00AD, // Soft Hyphen
        0x061C, // Arabic Letter Mark
        0x180E, // Mongolian Vowel Separator
        0xFFFE, // Invalid
        0xFFFF, // Invalid
        0xFFEF, // Reserved / invalid
        0x202A, // Left-to-Right Embedding
        0x202B, // Right-to-Left Embedding
        0x202C, // Pop Directional Formatting
        0x202D, // Left-to-Right Override
        0x202E, // Right-to-Left Override
        0x2066, // Left-to-Right Isolate
        0x2067, // Right-to-Left Isolate
        0x2068, // First Strong Isolate
        0x2069, // Pop Directional Isolate
    ];

    /// <summary>Character substitutions applied during hygiene.</summary>
    private static readonly Dictionary<char, string> Substitutions = new()
    {
        ['\u00A0'] = " ",   // non-breaking space → space
        ['\u2007'] = " ",   // figure space → space
        ['\u202F'] = " ",   // narrow no-break space → space
        ['\u2014'] = "-",   // em-dash → hyphen
        ['\u2013'] = "-",   // en-dash → hyphen
        ['\u2015'] = "-",   // horizontal bar → hyphen
        ['\u2018'] = "'",   // left single quote → '
        ['\u2019'] = "'",   // right single quote → '
        ['\u201A'] = "'",   // single low-9 quote → '
        ['\u201B'] = "'",   // single high-reversed-9 → '
        ['\u201C'] = "\"",  // left double quote → "
        ['\u201D'] = "\"",  // right double quote → "
        ['\u201E'] = "\"",  // double low-9 quote → "
        ['\u201F'] = "\"",  // double high-reversed-9 → "
        ['\u2032'] = "'",   // prime → '
        ['\u2033'] = "\"",  // double prime → "
        ['\u2026'] = "...", // ellipsis → three dots
    };

    /// <summary>Human-readable descriptions for stripped code points.</summary>
    private static string DescribeCodePoint(int cp) => cp switch
    {
        0x200B => "Zero-Width Space",
        0x200C => "Zero-Width Non-Joiner",
        0x200D => "Zero-Width Joiner",
        0x200E => "Left-to-Right Mark",
        0x200F => "Right-to-Left Mark",
        0x2060 => "Word Joiner",
        >= 0x2061 and <= 0x2064 => "Invisible Function Application character",
        0xFEFF => "Byte Order Mark / Zero-Width No-Break Space",
        0x00AD => "Soft Hyphen",
        0x061C => "Arabic Letter Mark",
        0x180E => "Mongolian Vowel Separator",
        0xFFFE or 0xFFFF or 0xFFEF => "Invalid Unicode character",
        >= 0x202A and <= 0x202E => "Bidirectional formatting character",
        >= 0x2066 and <= 0x2069 => "Bidirectional isolate character",
        _ => "Invisible / formatting character",
    };

    /// <inheritdoc />
    public TextCleanResult Clean(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            return TextCleanResult.Empty;
        }

        var (stripped, removed) = StripAndSubstitute(input);

        // NFKC normalization (fullwidth → ASCII, ligatures, etc.).
        string normalized = stripped.IsNormalized(NormalizationForm.FormKC)
            ? stripped
            : stripped.Normalize(NormalizationForm.FormKC);

        if (!ReferenceEquals(normalized, stripped) && normalized != stripped)
        {
            removed.Add(new RemovedItem("nfkc-normalization", 0, 0, "Applied NFKC Unicode normalization"));
        }

        double confidence = removed.Count == 0 ? 0.0 : Math.Min(1.0, 0.5 + (removed.Count * 0.05));
        return new TextCleanResult(input, normalized, removed, [], confidence);
    }

    private static (string Cleaned, List<RemovedItem> Removed) StripAndSubstitute(string input)
    {
        var sb = new StringBuilder(input.Length);
        var removed = new List<RemovedItem>();

        int index = 0;
        while (index < input.Length)
        {
            char c = input[index];

            // Handle surrogate pairs for code point checks.
            int codePoint;
            int charLen;
            if (char.IsHighSurrogate(c) && index + 1 < input.Length && char.IsLowSurrogate(input[index + 1]))
            {
                codePoint = char.ConvertToUtf32(c, input[index + 1]);
                charLen = 2;
            }
            else
            {
                codePoint = c;
                charLen = 1;
            }

            if (StrippedCodePoints.Contains(codePoint))
            {
                removed.Add(new RemovedItem("unicode-strip", index, charLen, DescribeCodePoint(codePoint)));
                index += charLen;
                continue;
            }

            if (charLen == 1 && Substitutions.TryGetValue(c, out string? replacement))
            {
                sb.Append(replacement);
                removed.Add(new RemovedItem("unicode-substitute", index, 1, $"Normalized '{Escape(c)}' → \"{replacement}\""));
                index += 1;
                continue;
            }

            sb.Append(input, index, charLen);
            index += charLen;
        }

        return (sb.ToString(), removed);
    }

    private static string Escape(char c) => $"U+{(int)c:X4}";
}
