using FluentAssertions;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Text;
using Xunit;

namespace WatermarkRemover.Text.Tests;

public class StatisticalWatermarkRewriterTests
{
    private readonly StatisticalWatermarkRewriter _rewriter = new();

    private static TextCleanOptions HeuristicOnly => new()
    {
        EnableStatistical = true,
        EnableHeuristicParaphrase = true,
        LlmEndpoint = null,
    };

    [Fact]
    public async Task Rewrite_SwapsEnglishGreenListToken()
    {
        TextCleanResult result = await _rewriter.RewriteAsync("We utilize the system.", HeuristicOnly);

        result.Cleaned.Should().NotContain("utilize");
        result.RemovedItems.Should().Contain(i => i.Type == "statistical-rewrite");
    }

    [Fact]
    public async Task Rewrite_SwapsRussianGreenListToken()
    {
        TextCleanResult result = await _rewriter.RewriteAsync("Это значимый результат.", HeuristicOnly);

        result.Cleaned.Should().NotContain("значимый");
        result.RemovedItems.Should().Contain(i => i.Type == "statistical-rewrite");
    }

    [Fact]
    public async Task Rewrite_PreservesRussianSentenceStructure()
    {
        const string input = "Это значимый результат.";
        TextCleanResult result = await _rewriter.RewriteAsync(input, HeuristicOnly);

        result.Cleaned.Should().StartWith("Это ");
        result.Cleaned.Should().EndWith("результат.");
    }

    [Fact]
    public async Task Rewrite_EmptyInput_ReturnsEmpty()
    {
        TextCleanResult result = await _rewriter.RewriteAsync(string.Empty, HeuristicOnly);
        result.Cleaned.Should().BeEmpty();
    }

    [Fact]
    public async Task Rewrite_NoGreenListTokens_LeavesTextIntact()
    {
        const string input = "A short plain sentence.";
        TextCleanResult result = await _rewriter.RewriteAsync(input, HeuristicOnly);

        result.Cleaned.Should().Be(input);
    }
}
