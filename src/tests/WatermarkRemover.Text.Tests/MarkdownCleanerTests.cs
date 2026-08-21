using FluentAssertions;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Text.Markdown;
using Xunit;

namespace WatermarkRemover.Text.Tests;

public class MarkdownCleanerTests
{
    private readonly MarkdownCleaner _cleaner = new();

    [Fact]
    public void Clean_PreservesFencedCodeBlocks()
    {
        const string md = "# Title\n\n```csharp\nvar x = 1;\n```\n";
        MarkdownCleanResult result = _cleaner.Clean(md);

        result.Cleaned.Should().Contain("var x = 1;");
        result.CodeBlocksPreserved.Should().Be(result.CodeBlocksFound);
        result.CodeBlocksFound.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Clean_StripsHeadingMarkersByDefault()
    {
        const string md = "# Heading\n\nBody text.";
        MarkdownCleanResult result = _cleaner.Clean(md);

        result.Cleaned.Should().NotContain("# Heading");
        result.Cleaned.Should().Contain("Heading");
    }

    [Fact]
    public void Clean_StripAll_RemovesCodeFences()
    {
        const string md = "```\ncode\n```";
        MarkdownCleanResult result = _cleaner.Clean(md, MarkdownCleanOptions.StripAll());

        result.Cleaned.Should().NotContain("```");
    }

    [Fact]
    public void Clean_RemovesInvisibleCharactersInsideCodeBlocks()
    {
        const string md = "```\nva\u200Br x = 1;\n```";
        MarkdownCleanResult result = _cleaner.Clean(md);

        result.Cleaned.Should().NotContain("\u200B");
    }

    [Fact]
    public void Clean_StripsFrontmatter()
    {
        const string md = "---\ntitle: Test\n---\n\nBody.";
        MarkdownCleanResult result = _cleaner.Clean(md);

        result.FrontmatterRemoved.Should().BeTrue();
        result.Cleaned.Should().NotContain("title: Test");
    }

    [Fact]
    public void Detect_ReturnsArtifactsList()
    {
        IReadOnlyList<AiArtifact> artifacts = _cleaner.Detect("# Title\n\nPlain body.");
        artifacts.Should().NotBeNull();
    }

    [Fact]
    public void Clean_HandlesRussianMarkdown()
    {
        const string md = "# Заголовок\n\nОбычный текст.";
        MarkdownCleanResult result = _cleaner.Clean(md);

        result.Cleaned.Should().Contain("Заголовок");
        result.Cleaned.Should().Contain("Обычный текст.");
    }
}
