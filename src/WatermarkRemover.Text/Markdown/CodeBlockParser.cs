namespace WatermarkRemover.Text.Markdown;

/// <summary>A parsed segment of a markdown document.</summary>
internal abstract record MarkdownSegment;

/// <summary>Plain (non-code) markdown text; subject to markdown transforms.</summary>
internal sealed record TextSegment(List<string> Lines) : MarkdownSegment;

/// <summary>
/// A fenced code block; content is preserved and only forced invisible-character
/// cleanup is applied. <see cref="FenceMarker"/> is the opening fence line and
/// <see cref="ClosingFence"/> the closing line (may be null when unterminated).
/// </summary>
internal sealed record CodeSegment(
    string FenceMarker,
    string? ClosingFence,
    List<string> ContentLines) : MarkdownSegment;

/// <summary>
/// Splits markdown into text and fenced-code segments. Handles ``` and ~~~ fences,
/// indented fences, info strings (language tags) and nested/unterminated fences per
/// CommonMark closing rules (same fence char, length &gt;= opening).
/// </summary>
internal static class CodeBlockParser
{
    public static List<MarkdownSegment> Parse(string text)
    {
        string[] lines = text.Split('\n');
        var segments = new List<MarkdownSegment>();
        var currentText = new List<string>();

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            if (TryGetFence(line, out char fenceChar, out int fenceLen, out _))
            {
                // Flush pending text.
                if (currentText.Count > 0)
                {
                    segments.Add(new TextSegment([.. currentText]));
                    currentText.Clear();
                }

                string opening = line;
                var content = new List<string>();
                int j = i + 1;
                string? closing = null;
                while (j < lines.Length)
                {
                    if (IsClosingFence(lines[j], fenceChar, fenceLen))
                    {
                        closing = lines[j];
                        break;
                    }

                    content.Add(lines[j]);
                    j++;
                }

                segments.Add(new CodeSegment(opening, closing, content));
                i = closing is null ? lines.Length : j + 1;
            }
            else
            {
                currentText.Add(line);
                i++;
            }
        }

        if (currentText.Count > 0)
        {
            segments.Add(new TextSegment([.. currentText]));
        }

        return segments;
    }

    private static bool TryGetFence(string line, out char fenceChar, out int fenceLen, out string info)
    {
        fenceChar = '\0';
        fenceLen = 0;
        info = string.Empty;

        int indent = 0;
        while (indent < line.Length && indent < 3 && line[indent] == ' ')
        {
            indent++;
        }

        if (indent >= line.Length)
        {
            return false;
        }

        char c = line[indent];
        if (c is not ('`' or '~'))
        {
            return false;
        }

        int k = indent;
        while (k < line.Length && line[k] == c)
        {
            k++;
        }

        int count = k - indent;
        if (count < 3)
        {
            return false;
        }

        fenceChar = c;
        fenceLen = count;
        info = line[k..].Trim();
        return true;
    }

    private static bool IsClosingFence(string line, char fenceChar, int openingLen)
    {
        int indent = 0;
        while (indent < line.Length && indent < 3 && line[indent] == ' ')
        {
            indent++;
        }

        int k = indent;
        while (k < line.Length && line[k] == fenceChar)
        {
            k++;
        }

        int count = k - indent;
        if (count < openingLen)
        {
            return false;
        }

        // A closing fence must not contain an info string.
        return line[k..].Trim().Length == 0;
    }
}
