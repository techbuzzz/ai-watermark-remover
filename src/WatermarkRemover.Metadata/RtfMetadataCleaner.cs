using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips authorship and provenance metadata control words from Rich Text Format
/// (<c>.rtf</c>) files. The cleaner walks the RTF stream as a character-by-character
/// parser, identifies the canonical set of metadata control words
/// (<c>\author</c>, <c>\generator</c>, <c>\doccomm</c>, <c>\company</c>,
/// <c>\manager</c>, <c>\category</c>, <c>\keywords</c>, <c>\subject</c>,
/// <c>\title</c>, <c>\comment</c>, <c>\hlinkbase</c>, <c>\operator</c>,
/// <c>\version</c>, <c>\edmins</c>, <c>\nofpages</c>, <c>\nofwords</c>,
/// <c>\nofchars</c>, <c>\nofcharsws</c>, <c>\id</c>) plus the compound
/// time-table entries (<c>\creatim</c>, <c>\revtbl</c>, <c>\printim</c>,
/// <c>\buptim</c>) that are followed by sub-control words like
/// <c>\yr</c>/<c>\mo</c>/<c>\dy</c>/<c>\hr</c>/<c>\min</c>/<c>\sec</c>,
/// and removes the control word together with its value. The RTF body, font
/// table, colour table, stylesheet, headers/footers, and visible text content
/// are all preserved byte-for-byte; only the metadata control words go away.
/// </summary>
public sealed class RtfMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".rtf"];

    /// <summary>RTF file magic. Every valid RTF stream starts with the literal text <c>{\rtf</c>.</summary>
    private const string RtfMagic = "{\\rtf";

    /// <summary>
    /// RTF control words that carry authorship or provenance metadata.
    /// Matched case-sensitively (RTF control words are case-sensitive per the
    /// spec; every real-world producer emits these in lowercase).
    /// </summary>
    private static readonly HashSet<string> MetadataControlWords = new(StringComparer.Ordinal)
    {
        "title", "subject", "author", "manager", "company", "category",
        "keywords", "comment", "doccomm", "hlinkbase", "generator",
        "operator", "version", "edmins", "nofpages", "nofwords",
        "nofchars", "nofcharsws", "id",
    };

    /// <summary>
    /// Compound metadata control words followed by a sequence of sub-control words
    /// (e.g. <c>\creatim\yr2024\mo1\dy15\hr10\min30\sec0</c>) rather than a
    /// space-delimited value. The cleaner strips the compound control word and
    /// every sub-control word up to the next group delimiter or non-sub word.
    /// </summary>
    private static readonly HashSet<string> CompoundControlWords = new(StringComparer.Ordinal)
    {
        "creatim", "revtbl", "printim", "buptim",
    };

    /// <summary>Sub-control words valid inside a compound time-table block.</summary>
    private static readonly HashSet<string> CompoundSubControlWords = new(StringComparer.Ordinal)
    {
        "yr", "mo", "dy", "hr", "min", "sec",
    };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) => Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        var sw = Stopwatch.StartNew();
        long inputSize = new FileInfo(inputPath).Length;
        var removed = new List<MetadataEntry>();

        string rtf = ReadRtf(inputPath);
        ValidateMagic(rtf, inputPath);

        string cleaned = StripMetadata(rtf, removed);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        WriteRtf(finalOut, cleaned);

        sw.Stop();
        long outputSize = new FileInfo(finalOut).Length;
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, outputSize, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        string rtf = ReadRtf(inputPath);
        ValidateMagic(rtf, inputPath);

        var found = new List<MetadataEntry>();
        StripMetadata(rtf, found);
        return found;
    }

    private static string ReadRtf(string path)
    {
        try
        {
            // RTF spec says it must be 7-bit ASCII; we still use a tolerant reader.
            return File.ReadAllText(path, Encoding.ASCII);
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to read RTF: {path}", ex) { FilePath = path };
        }
    }

    private static void WriteRtf(string path, string content)
    {
        try
        {
            // Write as plain ASCII (the RTF spec uses only 7-bit ASCII for the
            // metadata control words we touch); fall back to UTF-8 without BOM
            // if a higher-bit value happens to be present elsewhere.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to write RTF: {path}", ex) { FilePath = path };
        }
    }

    private static void ValidateMagic(string rtf, string path)
    {
        if (rtf.Length < RtfMagic.Length || !rtf.StartsWith(RtfMagic, StringComparison.Ordinal))
        {
            throw new MetadataStripException(
                $"Not a valid RTF file: missing '{RtfMagic}' header.")
            {
                FilePath = path,
            };
        }
    }

    private static string StripMetadata(string rtf, List<MetadataEntry> removed)
    {
        var output = new StringBuilder(rtf.Length);
        int i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '\\')
            {
                int wordStart = i;
                i++;
                if (i >= rtf.Length)
                {
                    // Trailing backslash — emit and stop.
                    output.Append(rtf, wordStart, i - wordStart);
                    break;
                }

                char first = rtf[i];
                if (first == '\'')
                {
                    // \'hh hex char — keep as-is.
                    i++;
                    if (i + 1 < rtf.Length)
                    {
                        i += 2;
                    }

                    output.Append(rtf, wordStart, i - wordStart);
                    continue;
                }

                if (!char.IsLetter(first))
                {
                    // Special control symbol: \\, \{, \}, \~, \-, \_, etc.
                    // Keep the single character as-is.
                    i++;
                    output.Append(rtf, wordStart, i - wordStart);
                    continue;
                }

                int nameStart = i;
                while (i < rtf.Length && char.IsLetter(rtf[i]))
                {
                    i++;
                }

                string name = rtf.Substring(nameStart, i - nameStart);

                int paramStart = i;
                if (i < rtf.Length && rtf[i] == '-')
                {
                    i++;
                }

                while (i < rtf.Length && char.IsDigit(rtf[i]))
                {
                    i++;
                }

                bool hasParam = i > paramStart;

                if (MetadataControlWords.Contains(name))
                {
                    string value = StripValue(rtf, ref i, hasParam, paramStart);
                    removed.Add(new MetadataEntry("RTF/info", name, value));
                    // Don't emit anything for this control word.
                    continue;
                }

                if (CompoundControlWords.Contains(name))
                {
                    // Strip the compound control word and its sub-control words.
                    int compoundEnd = StripCompoundSubWords(rtf, i);
                    i = compoundEnd;
                    removed.Add(new MetadataEntry("RTF/info", name, "<compound>"));
                    continue;
                }

                // Not a metadata control word — emit verbatim.
                output.Append(rtf, wordStart, i - wordStart);
                continue;
            }

            // Any other character (text, braces, whitespace, punctuation): emit.
            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>
    /// Consume the value that follows a metadata control word and return it as a
    /// display string. If the control word had a numeric parameter, the parameter
    /// is the value (no further reading needed). Otherwise, if the next character
    /// is a space, the value is the space-delimited text up to the next
    /// control word or group delimiter.
    /// </summary>
    private static string StripValue(string rtf, ref int i, bool hasParam, int paramStart)
    {
        if (hasParam)
        {
            return rtf.Substring(paramStart, i - paramStart);
        }

        if (i < rtf.Length && rtf[i] == ' ')
        {
            // The space is a delimiter; consume it, then read the value text.
            i++;
            int valueStart = i;
            while (i < rtf.Length && rtf[i] != '\\' && rtf[i] != '{' && rtf[i] != '}')
            {
                i++;
            }

            return rtf.Substring(valueStart, i - valueStart);
        }

        return string.Empty;
    }

    /// <summary>
    /// After a compound metadata control word has been consumed, walk forward
    /// past its sub-control words (e.g. <c>\yr2024\mo1\dy15</c>) and return the
    /// new cursor position. Stops at the first character that is not part of a
    /// recognised sub-control word or the whitespace between them.
    /// </summary>
    private static int StripCompoundSubWords(string rtf, int start)
    {
        int i = start;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '{' || c == '}')
            {
                break;
            }

            if (c == '\\')
            {
                int subStart = i;
                i++;
                if (i >= rtf.Length || !char.IsLetter(rtf[i]))
                {
                    return subStart;
                }

                int subNameStart = i;
                while (i < rtf.Length && char.IsLetter(rtf[i]))
                {
                    i++;
                }

                string subName = rtf.Substring(subNameStart, i - subNameStart);
                if (i < rtf.Length && rtf[i] == '-')
                {
                    i++;
                }

                while (i < rtf.Length && char.IsDigit(rtf[i]))
                {
                    i++;
                }

                if (!CompoundSubControlWords.Contains(subName))
                {
                    return subStart;
                }
            }
            else if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                i++;
            }
            else
            {
                break;
            }
        }

        return i;
    }
}
