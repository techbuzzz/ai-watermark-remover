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
        [new DeepSeekWatermarkDetector()],
        [new GrokWatermarkDetector()],
        [new MistralWatermarkDetector()],
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

    // -------- DeepSeek --------

    [Fact]
    public void DeepSeek_ThinkTag_DetectedAndStripped()
    {
        var detector = new DeepSeekWatermarkDetector();
        const string input = "<think>hidden reasoning</think>Public answer.";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().Contain(m =>
            m.Vendor == "DeepSeek" &&
            m.Pattern == "reasoning-block");
        string cleaned = detector.Remove(input, matches);
        // The tags themselves are stripped, but the content between
        // them (the chain-of-thought) is preserved — that's the
        // text the user actually wanted to see.
        cleaned.Should().NotContain("<think>");
        cleaned.Should().NotContain("</think>");
        cleaned.Should().Contain("hidden reasoning");
        cleaned.Should().Contain("Public answer.");
    }

    [Fact]
    public void DeepSeek_ThinkTag_ContentPreserved()
    {
        var detector = new DeepSeekWatermarkDetector();
        const string input = "<think>The user asked a question.</think>The answer is 42.";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);
        found.Should().BeTrue();
        string cleaned = detector.Remove(input, matches);

        cleaned.Should().Contain("The answer is 42.");
    }

    [Fact]
    public void DeepSeek_FullwidthComma_Flagged()
    {
        var detector = new DeepSeekWatermarkDetector();
        const string input = "Hello\uFF0Cworld"; // U+FF0C fullwidth comma between Latin letters

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().Contain(m => m.Pattern == "fullwidth-punctuation");
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().Be("Hello,world");
    }

    [Fact]
    public void DeepSeek_FullwidthTilde_Flagged()
    {
        var detector = new DeepSeekWatermarkDetector();
        const string input = "tilde like\uFF5Ethis"; // U+FF5E fullwidth tilde

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().Contain(m => m.Pattern == "fullwidth-punctuation");
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().Be("tilde like~this");
    }

    [Fact]
    public void DeepSeek_CleanText_NoMatches()
    {
        var detector = new DeepSeekWatermarkDetector();
        bool found = detector.Detect("A perfectly ordinary Latin sentence.", out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    [Fact]
    public void DeepSeek_MixedLatinFullwidth_SingleMatch()
    {
        var detector = new DeepSeekWatermarkDetector();
        // Single fullwidth comma in an otherwise-Latin sentence —
        // should produce exactly one fullwidth-punctuation match,
        // not a run of one.
        const string input = "Hello\uFF0Cworld";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Where(m => m.Pattern == "fullwidth-punctuation").Should().HaveCount(1);
    }

    // -------- Grok --------

    [Fact]
    public void Grok_EmojiBurst_Detected()
    {
        var detector = new GrokWatermarkDetector();
        // Three BMP emoji in a row (⚡⚡⚡).
        const string input = "\u26A1\u26A1\u26A1Hey there.";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().Contain(m => m.Vendor == "Grok" && m.Pattern == "emoji-burst");
    }

    [Fact]
    public void Grok_EmojiBurst_CollapsedToOne()
    {
        var detector = new GrokWatermarkDetector();
        const string input = "\u26A1\u26A1\u26A1\u26A1hello";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);
        found.Should().BeTrue();
        string cleaned = detector.Remove(input, matches);
        // After collapse we should have exactly one lightning bolt,
        // not four.
        cleaned.Should().Be("\u26A1hello");
    }

    [Fact]
    public void Grok_EmDashCluster_DetectedAndCollapsed()
    {
        var detector = new GrokWatermarkDetector();
        const string input = "well\u2014\u2014\u2014that's odd";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().Contain(m => m.Pattern == "em-dash-cluster");
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().Be("well\u2014that's odd");
    }

    [Fact]
    public void Grok_SingleEmoji_DoesNotMatch()
    {
        var detector = new GrokWatermarkDetector();
        const string input = "Just one \u26A1 here.";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        // 1 emoji is not suspicious on its own — Grok's signature
        // is the burst of 3+.
        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Grok_SingleEmDash_DoesNotMatch()
    {
        var detector = new GrokWatermarkDetector();
        const string input = "An em-dash\u2014just one.";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Grok_CleanText_NoMatches()
    {
        var detector = new GrokWatermarkDetector();
        bool found = detector.Detect("Plain prose, no emoji, no clusters.", out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    // -------- Mistral --------

    [Fact]
    public void Mistral_InstBlock_DetectedAndStripped()
    {
        var detector = new MistralWatermarkDetector();
        const string input = "Hello [INST] user content [/INST] world";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.Vendor == "Mistral" && m.Pattern == "template-leak");
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().NotContain("[INST]");
        cleaned.Should().NotContain("[/INST]");
        cleaned.Should().Contain("user content");
    }

    [Fact]
    public void Mistral_SysBlock_DetectedAndStripped()
    {
        var detector = new MistralWatermarkDetector();
        const string input = "<<SYS>>system prompt<</SYS>>visible text";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().HaveCount(2);
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().NotContain("<<SYS>>");
        cleaned.Should().NotContain("<</SYS>>");
        cleaned.Should().Contain("system prompt");
        cleaned.Should().Contain("visible text");
    }

    [Fact]
    public void Mistral_SentencePieceMarkers_DetectedAndStripped()
    {
        var detector = new MistralWatermarkDetector();
        const string input = "<s>begin of turn</s>";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        matches.Should().HaveCount(2);
        string cleaned = detector.Remove(input, matches);
        cleaned.Should().Be("begin of turn");
    }

    [Fact]
    public void Mistral_MultipleInst_EachGetsItsOwnMatch()
    {
        var detector = new MistralWatermarkDetector();
        const string input = "[INST] one [/INST] mid [INST] two [/INST] end";

        bool found = detector.Detect(input, out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeTrue();
        // Two [INST] + two [/INST] = 4 template-leak matches.
        matches.Should().HaveCount(4);
        matches.Count(m => m.Length == 6).Should().Be(2); // two [INST]
        matches.Count(m => m.Length == 7).Should().Be(2); // two [/INST]
    }

    [Fact]
    public void Mistral_CleanText_NoMatches()
    {
        var detector = new MistralWatermarkDetector();
        bool found = detector.Detect("A perfectly normal English paragraph with no markers.", out IReadOnlyList<WatermarkMatch> matches);

        found.Should().BeFalse();
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Mistral_Remove_EmptyMatches_IsNoOp()
    {
        var detector = new MistralWatermarkDetector();
        const string input = "Nothing to see here.";
        IReadOnlyList<WatermarkMatch> empty = [];

        string cleaned = detector.Remove(input, empty);
        cleaned.Should().Be(input);
    }
}
