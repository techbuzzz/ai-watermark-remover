using FluentAssertions;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Text.Vendors;
using Xunit;

namespace WatermarkRemover.Text.Tests;

public class VendorDetectorTests
{
    public static IEnumerable<object[]> Detectors =>
    [
        [new ClaudeWatermarkDetector()],
        [new GeminiWatermarkDetector()],
        [new OpenAiWatermarkDetector()],
    ];

    [Fact]
    public void Claude_DetectsAndRemovesZeroWidthRun()
    {
        var detector = new ClaudeWatermarkDetector();
        string input = "word\u200B\u200Cword";
        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().OnlyContain(m => m.Vendor == "Claude");
        detector.Remove(input, matches).Should().Be("wordword");
    }

    [Fact]
    public void Gemini_DetectsBoundaryZeroWidthMarker()
    {
        var detector = new GeminiWatermarkDetector();
        string input = "word \u200B word"; // isolated zero-width at a whitespace boundary
        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        detector.Remove(input, matches).Should().Be("word  word");
    }

    [Fact]
    public void OpenAi_DetectsMidStreamWordJoiner()
    {
        var detector = new OpenAiWatermarkDetector();
        string input = "word\u2060word"; // word joiner mid-stream
        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        detector.Remove(input, matches).Should().Be("wordword");
    }

    [Theory]
    [MemberData(nameof(Detectors))]
    public void Detect_CleanText_ReturnsFalse(IAiTextWatermarkDetector detector)
    {
        bool found = detector.Detect("Perfectly ordinary sentence.", out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    [Fact]
    public void ClaudeDetector_HomoglyphBetweenLatin_IsRemoved()
    {
        var detector = new ClaudeWatermarkDetector();
        string input = "b\u0430d"; // cyrillic 'а' between Latin letters
        detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);
        string cleaned = detector.Remove(input, matches);

        cleaned.Should().Be("bad");
    }

    [Fact]
    public void ClaudeDetector_CyrillicWord_IsPreserved()
    {
        var detector = new ClaudeWatermarkDetector();
        const string input = "Привет";
        detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);
        string cleaned = detector.Remove(input, matches);

        cleaned.Should().Be(input);
    }
}
