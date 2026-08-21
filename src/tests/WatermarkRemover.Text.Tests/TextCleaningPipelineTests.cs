using FluentAssertions;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Text;
using WatermarkRemover.Text.Vendors;
using Xunit;

namespace WatermarkRemover.Text.Tests;

public class TextCleaningPipelineTests
{
    private static TextCleaningPipeline BuildPipeline() => new(
        new UnicodeHygieneCleaner(),
        new StatisticalWatermarkRewriter(),
        [new ClaudeWatermarkDetector(), new GeminiWatermarkDetector(), new OpenAiWatermarkDetector()]);

    [Fact]
    public async Task Clean_RemovesInvisibleCharacters_ByDefault()
    {
        TextCleaningPipeline pipeline = BuildPipeline();
        TextCleanResult result = await pipeline.CleanAsync("Hello\u200Bworld");

        result.Cleaned.Should().Be("Helloworld");
    }

    [Fact]
    public async Task Clean_EmptyInput_ReturnsEmptyResult()
    {
        TextCleaningPipeline pipeline = BuildPipeline();
        TextCleanResult result = await pipeline.CleanAsync(string.Empty);

        result.Cleaned.Should().BeEmpty();
    }

    [Fact]
    public async Task Clean_WithStatisticalEnabled_RewritesRussianSynonyms()
    {
        TextCleaningPipeline pipeline = BuildPipeline();
        TextCleanResult result = await pipeline.CleanAsync(
            "Это значимый вклад.",
            new TextCleanOptions { EnableStatistical = true, EnableHeuristicParaphrase = true });

        result.Cleaned.Should().NotContain("значимый");
    }

    [Fact]
    public async Task Clean_NullText_Throws()
    {
        TextCleaningPipeline pipeline = BuildPipeline();
        Func<Task> act = async () => await pipeline.CleanAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Detect_ReturnsListWithoutThrowing()
    {
        TextCleaningPipeline pipeline = BuildPipeline();
        IReadOnlyList<WatermarkMatch> matches = pipeline.Detect("Ordinary text with no watermark.");

        matches.Should().NotBeNull();
    }
}
