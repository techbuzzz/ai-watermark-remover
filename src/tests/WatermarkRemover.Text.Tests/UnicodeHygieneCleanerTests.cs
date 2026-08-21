using FluentAssertions;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Text;
using Xunit;

namespace WatermarkRemover.Text.Tests;

public class UnicodeHygieneCleanerTests
{
    private readonly UnicodeHygieneCleaner _cleaner = new();

    [Fact]
    public void Clean_RemovesZeroWidthSpace()
    {
        string input = "Hel\u200Blo";
        TextCleanResult result = _cleaner.Clean(input);

        result.Cleaned.Should().Be("Hello");
        result.RemovedItems.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("\u200B")] // zero-width space
    [InlineData("\u200C")] // zero-width non-joiner
    [InlineData("\u200D")] // zero-width joiner
    [InlineData("\u2060")] // word joiner
    [InlineData("\uFEFF")] // BOM / zero-width no-break space
    [InlineData("\u00AD")] // soft hyphen
    public void Clean_StripsInvisibleCodePoints(string invisible)
    {
        string input = $"a{invisible}b";
        TextCleanResult result = _cleaner.Clean(input);

        result.Cleaned.Should().Be("ab");
    }

    [Fact]
    public void Clean_EmptyInput_ReturnsEmpty()
    {
        TextCleanResult result = _cleaner.Clean(string.Empty);
        result.Cleaned.Should().BeEmpty();
    }

    [Fact]
    public void Clean_PlainAscii_IsUnchanged()
    {
        const string input = "The quick brown fox.";
        TextCleanResult result = _cleaner.Clean(input);

        result.Cleaned.Should().Be(input);
        result.RemovedItems.Should().BeEmpty();
    }

    [Fact]
    public void Clean_PreservesGenuineCyrillicText()
    {
        // A wholly Cyrillic word must NOT be mangled into Latin.
        const string input = "Привет мир";
        TextCleanResult result = _cleaner.Clean(input);

        result.Cleaned.Should().Be(input);
    }
}
