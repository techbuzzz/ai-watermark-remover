using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text.Markdown;

/// <summary>
/// Cleans markdown documents. Preserves fenced code blocks (only forced invisible-character
/// cleanup is applied inside them) while applying the 20 toggleable transforms to normal text,
/// and detects/removes AI-specific artifacts regardless of toggles.
/// </summary>
public sealed partial class MarkdownCleaner : IMarkdownCleaner
{
    private readonly UnicodeHygieneCleaner _unicode = new();

    /// <summary>Invisible characters force-stripped everywhere (including code blocks).</summary>
    /// <remarks>
    /// <see cref="SearchValues{Char}"/> is a purpose-built, vectorised read-only set lookup
    /// that beats <see cref="Array.IndexOf{T}"/> on every call site — the hot paths in
    /// <see cref="ForceStripInvisible"/> and <see cref="DetectInvisibleInCode"/> each
    /// iterate per character, so this matters for large markdown documents.
    /// </remarks>
    private static readonly SearchValues<char> ForcedInvisible =
        SearchValues.Create(
        [
            '\u200B', '\u200C', '\u200D', '\u200E', '\u200F', '\u2060', '\uFEFF',
            '\u2061', '\u2062', '\u2063', '\u2064', '\u00AD', '\u061C', '\u180E',
            '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
            '\u2066', '\u2067', '\u2068', '\u2069',
        ]);

    /// <inheritdoc />
    public MarkdownCleanResult Clean(string markdown, MarkdownCleanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        options ??= new MarkdownCleanOptions();

        var removed = new List<RemovedItem>();
        var artifacts = new List<AiArtifact>();

        string working = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        // Frontmatter removal (must be at the very start).
        bool frontmatterRemoved = false;
        if (options.StripFrontmatter)
        {
            working = RemoveFrontmatter(working, removed, artifacts, out frontmatterRemoved);
        }

        List<MarkdownSegment> segments = CodeBlockParser.Parse(working);
        int codeBlocksFound = segments.Count(s => s is CodeSegment);
        int codeBlocksPreserved = 0;

        var output = new StringBuilder();
        for (int s = 0; s < segments.Count; s++)
        {
            MarkdownSegment segment = segments[s];
            if (segment is CodeSegment code)
            {
                bool preserved = ProcessCodeSegment(code, options, output, removed, artifacts);
                if (preserved)
                {
                    codeBlocksPreserved++;
                }
            }
            else if (segment is TextSegment text)
            {
                ProcessTextSegment(text, options, output, removed, artifacts);
            }
        }

        string cleaned = output.ToString();

        // AI signature / boilerplate detection operates on the assembled text body.
        if (options.StripAiSignatures)
        {
            cleaned = RemoveAiSignatures(cleaned, removed, artifacts);
        }

        cleaned = RemoveAiBoilerplate(cleaned, removed, artifacts);

        // Final tidy-up: collapse 3+ blank lines into a single blank line and trim edges.
        cleaned = MultiBlankLineRegex().Replace(cleaned, "\n\n").Trim('\n');
        if (options.StripTrailingWs)
        {
            cleaned += "\n";
        }

        return new MarkdownCleanResult(markdown, cleaned, removed, artifacts, codeBlocksFound, codeBlocksPreserved, frontmatterRemoved);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiArtifact> Detect(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var artifacts = new List<AiArtifact>();
        var dummyRemoved = new List<RemovedItem>();
        string working = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        // Detect (without altering) — reuse the same detectors.
        List<MarkdownSegment> segments = CodeBlockParser.Parse(working);
        int lineOffset = 0;
        foreach (MarkdownSegment segment in segments)
        {
            switch (segment)
            {
                case CodeSegment code:
                    DetectInvisibleInCode(code, lineOffset, artifacts);
                    lineOffset += code.ContentLines.Count + 1 + (code.ClosingFence is null ? 0 : 1);
                    break;
                case TextSegment text:
                    DetectInvisibleSeparators(text, lineOffset, artifacts);
                    lineOffset += text.Lines.Count;
                    break;
            }
        }

        RemoveAiSignatures(working, dummyRemoved, artifacts);
        RemoveAiBoilerplate(working, dummyRemoved, artifacts);
        return artifacts;
    }

    // ---------------------------------------------------------------- code segments

    private bool ProcessCodeSegment(CodeSegment code, MarkdownCleanOptions options, StringBuilder output, List<RemovedItem> removed, List<AiArtifact> artifacts)
    {
        // Forced invisible-character cleanup inside code (always, regardless of toggles).
        var cleanedLines = new List<string>(code.ContentLines.Count);
        foreach (string line in code.ContentLines)
        {
            string cleaned = ForceStripInvisible(line, out int stripped);
            if (stripped > 0)
            {
                removed.Add(new RemovedItem("code-invisible", 0, stripped, $"Removed {stripped} invisible char(s) inside code block"));
                artifacts.Add(new AiArtifact("code-block-invisible", "Invisible/bidi character inside code block", 0, 0));
            }

            cleanedLines.Add(cleaned);
        }

        if (options.StripCodeFences)
        {
            // Drop the fence markers, keep content.
            foreach (string line in cleanedLines)
            {
                output.Append(line).Append('\n');
            }

            removed.Add(new RemovedItem("code-fence", 0, 0, "Removed code fence markers (content preserved)"));
            return false;
        }

        // Preserve fence + language tag.
        output.Append(code.FenceMarker).Append('\n');
        foreach (string line in cleanedLines)
        {
            output.Append(line).Append('\n');
        }

        if (code.ClosingFence is not null)
        {
            output.Append(code.ClosingFence).Append('\n');
        }

        return true;
    }

    private static void DetectInvisibleInCode(CodeSegment code, int lineOffset, List<AiArtifact> artifacts)
    {
        for (int i = 0; i < code.ContentLines.Count; i++)
        {
            if (code.ContentLines[i].Any(c => ForcedInvisible.Contains(c)))
            {
                artifacts.Add(new AiArtifact("code-block-invisible", "Invisible/bidi character inside code block", lineOffset + i + 2, 0));
            }
        }
    }

    // ---------------------------------------------------------------- text segments

    private void ProcessTextSegment(TextSegment text, MarkdownCleanOptions options, StringBuilder output, List<RemovedItem> removed, List<AiArtifact> artifacts)
    {
        string block = string.Join('\n', text.Lines);

        // Multiline HTML comments and [//]: # (...) comments.
        if (options.StripComments)
        {
            block = StripComments(block, removed);
        }

        // Layer A unicode across the whole block.
        if (options.ApplyUnicodeLayerA && options.StripUnicodeMd)
        {
            TextCleanResult unicodeResult = _unicode.Clean(block);
            if (unicodeResult.RemovedItems.Count > 0)
            {
                removed.AddRange(unicodeResult.RemovedItems);
            }

            block = unicodeResult.Cleaned;
        }
        else
        {
            block = ForceStripInvisible(block, out _);
        }

        // Detect invisible box-drawing separators before line transforms.
        DetectInvisibleSeparators(new TextSegment([.. block.Split('\n')]), 0, artifacts);

        var resultLines = new List<string>();
        foreach (string original in block.Split('\n'))
        {
            string? line = TransformLine(original, options, removed);
            if (line is not null)
            {
                resultLines.Add(line);
            }
        }

        foreach (string line in resultLines)
        {
            output.Append(line).Append('\n');
        }
    }

    private static string? TransformLine(string original, MarkdownCleanOptions options, List<RemovedItem> removed)
    {
        string line = original;

        // Invisible separator line (box drawing) → drop.
        if (IsInvisibleSeparatorLine(line))
        {
            removed.Add(new RemovedItem("separator-line", 0, line.Length, "Removed box-drawing separator line"));
            return null;
        }

        // Horizontal rule.
        if (options.StripHr && HrRegex().IsMatch(line))
        {
            removed.Add(new RemovedItem("hr", 0, line.Length, "Removed horizontal rule"));
            return null;
        }

        // Frontmatter-style comment lines [//]: # (...)
        if (options.StripComments && LinkRefCommentRegex().IsMatch(line))
        {
            removed.Add(new RemovedItem("comment", 0, line.Length, "Removed link-reference comment"));
            return null;
        }

        if (options.StripImages)
        {
            line = ImageRegex().Replace(line, m =>
            {
                removed.Add(new RemovedItem("image", m.Index, m.Length, "Removed image"));
                return string.Empty;
            });
        }

        if (options.StripLinks)
        {
            line = LinkRegex().Replace(line, m => m.Groups["text"].Value);
        }

        if (options.StripHtml || options.StripXmlTags)
        {
            line = HtmlTagRegex().Replace(line, m =>
            {
                removed.Add(new RemovedItem("html-tag", m.Index, m.Length, "Removed HTML/XML tag"));
                return string.Empty;
            });
        }

        if (options.StripTaskLists)
        {
            var match = TaskListRegex().Match(line);
            if (match.Success)
            {
                string rest = line[match.Length..].Trim();
                removed.Add(new RemovedItem("task-list", 0, match.Length, "Removed task-list checkbox"));
                if (rest.Length == 0)
                {
                    return null; // empty task item → drop entirely
                }

                line = match.Groups["indent"].Value + "- " + rest;
            }
        }

        if (options.StripHeadings)
        {
            var m = HeadingRegex().Match(line);
            if (m.Success)
            {
                removed.Add(new RemovedItem("heading", 0, m.Groups["hashes"].Length, "Removed heading marker"));
                line = m.Groups["content"].Value;
            }
        }

        if (options.StripBlockquotes)
        {
            var m = BlockquoteRegex().Match(line);
            if (m.Success)
            {
                removed.Add(new RemovedItem("blockquote", 0, m.Length, "Removed blockquote prefix"));
                line = line[m.Length..];
            }
        }

        if (options.StripTableSyntax && TableRowRegex().IsMatch(line))
        {
            if (TableSeparatorRegex().IsMatch(line))
            {
                return null; // drop the |---|---| separator row
            }

            line = string.Join("  ", line.Trim().Trim('|').Split('|').Select(c => c.Trim()));
            removed.Add(new RemovedItem("table", 0, 0, "Converted table row to plain text"));
        }

        if (options.NormalizeLists)
        {
            var m = UnorderedListRegex().Match(line);
            if (m.Success && m.Groups["bullet"].Value is "*" or "+")
            {
                line = m.Groups["indent"].Value + "- " + line[m.Length..];
            }
        }

        if (options.StripBoldItalic)
        {
            line = BoldItalicRegex().Replace(line, m => m.Groups["content"].Value);
        }

        if (options.StripInlineCode)
        {
            line = InlineCodeRegex().Replace(line, m => m.Groups["content"].Value);
        }

        if (options.StripMentions)
        {
            line = MentionRegex().Replace(line, m =>
            {
                removed.Add(new RemovedItem("mention", m.Index, m.Length, $"Removed {m.Value}"));
                return string.Empty;
            });
        }

        // Unwrap empty list items.
        if (options.UnwrapEmptyLists && EmptyListItemRegex().IsMatch(line))
        {
            removed.Add(new RemovedItem("empty-list-item", 0, line.Length, "Removed empty list item"));
            return null;
        }

        if (options.StripTrailingWs)
        {
            line = line.TrimEnd();
        }

        return line;
    }

    // ---------------------------------------------------------------- forced cleanup helpers

    private static string ForceStripInvisible(string input, out int stripped)
    {
        stripped = 0;
        if (input.Length == 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (ForcedInvisible.Contains(c))
            {
                stripped++;
                continue;
            }

            sb.Append(c);
        }

        return stripped == 0 ? input : sb.ToString();
    }

    private static bool IsInvisibleSeparatorLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        int boxCount = trimmed.Count(c => c is >= '\u2500' and <= '\u257F');
        return boxCount >= 3 && boxCount >= trimmed.Length - 2;
    }

    private static void DetectInvisibleSeparators(TextSegment text, int lineOffset, List<AiArtifact> artifacts)
    {
        for (int i = 0; i < text.Lines.Count; i++)
        {
            if (IsInvisibleSeparatorLine(text.Lines[i]))
            {
                artifacts.Add(new AiArtifact("invisible-separator", "Box-drawing separator line", lineOffset + i + 1, 0));
            }
        }
    }

    private static string RemoveFrontmatter(string text, List<RemovedItem> removed, List<AiArtifact> artifacts, out bool frontmatterRemoved)
    {
        frontmatterRemoved = false;
        var match = FrontmatterRegex().Match(text);
        if (match.Success)
        {
            frontmatterRemoved = true;
            removed.Add(new RemovedItem("frontmatter", 0, match.Length, "Removed YAML frontmatter"));
            artifacts.Add(new AiArtifact("frontmatter", "YAML frontmatter block", 1, 0));
            return text[match.Length..];
        }

        return text;
    }

    private static string StripComments(string block, List<RemovedItem> removed)
    {
        return HtmlCommentRegex().Replace(block, m =>
        {
            removed.Add(new RemovedItem("comment", m.Index, m.Length, "Removed HTML comment"));
            return string.Empty;
        });
    }

    private static string RemoveAiSignatures(string text, List<RemovedItem> removed, List<AiArtifact> artifacts)
    {
        var match = AiSignatureRegex().Match(text);
        if (match.Success)
        {
            removed.Add(new RemovedItem("ai-signature", match.Index, match.Length, "Removed AI tool signature block"));
            artifacts.Add(new AiArtifact("ai-signature", "AI tool attribution/signature", 0, 0));
            return text[..match.Index].TrimEnd() + "\n";
        }

        return text;
    }

    private static string RemoveAiBoilerplate(string text, List<RemovedItem> removed, List<AiArtifact> artifacts)
    {
        return AiBoilerplateRegex().Replace(text, m =>
        {
            removed.Add(new RemovedItem("ai-boilerplate", m.Index, m.Length, "Removed AI boilerplate/disclaimer"));
            artifacts.Add(new AiArtifact("ai-boilerplate", "AI boilerplate/disclaimer sentence", 0, 0));
            return string.Empty;
        });
    }

    // ---------------------------------------------------------------- regex definitions

    [GeneratedRegex(@"\A---\r?\n.*?\r?\n---[ \t]*\r?\n?", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^(?<hashes>#{1,6})[ \t]+(?<content>.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*>[ \t]?")]
    private static partial Regex BlockquoteRegex();

    [GeneratedRegex(@"^\s*([-*_])[ \t]*(\1[ \t]*){2,}$")]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"^\s*\[//\]:\s*#\s*\(.*\)\s*$")]
    private static partial Regex LinkRefCommentRegex();

    [GeneratedRegex(@"</?[a-zA-Z][a-zA-Z0-9-]*(\s[^>]*)?/?>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+][ \t]+\[[ xX]\][ \t]?")]
    private static partial Regex TaskListRegex();

    [GeneratedRegex(@"^(?<indent>\s*)(?<bullet>[-*+])[ \t]+")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^\s*[-*+][ \t]*$")]
    private static partial Regex EmptyListItemRegex();

    [GeneratedRegex(@"(\*\*|__|\*|_)(?<content>.+?)\1")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"`(?<content>[^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(?<=^|\s)[@#][A-Za-z0-9_]+")]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"^\s*\|.*\|\s*$")]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"^\s*\|?[ \t]*:?-{2,}:?[ \t]*(\|[ \t]*:?-{2,}:?[ \t]*)*\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlankLineRegex();

    [GeneratedRegex(@"(\n[ \t]*)*\n?(-{2,}\s*\n)?[ \t]*(🤖\s*)?(_?(Generated|Created|Written|Made|Produced)\s+(with|by|using)\s+(\[?)(ChatGPT|GPT-4|GPT-4o|Claude|Claude Code|Gemini|Bard|Copilot|Anthropic|OpenAI|Google AI|an AI assistant|AI)\b.*|Co-Authored-By:\s*(Claude|Copilot|ChatGPT|Gemini).*|🤖\s*Generated with.*)(\n.*)*\z", RegexOptions.IgnoreCase)]
    private static partial Regex AiSignatureRegex();

    [GeneratedRegex(@"(?im)^[ \t]*(As an AI( language)? model,?|I'm sorry,? but|I am sorry,? but|I cannot( and will not)?|I'm unable to|Please note that I,? as an AI|It's important to note that as an AI|Disclaimer:\s*As an AI)\b.*(\r?\n)?", RegexOptions.None)]
    private static partial Regex AiBoilerplateRegex();
}
