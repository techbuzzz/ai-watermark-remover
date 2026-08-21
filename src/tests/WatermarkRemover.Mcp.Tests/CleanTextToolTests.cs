using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Mcp.Tools;
using WatermarkRemover.Text;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// Unit tests for <see cref="CleanTextTool"/>. The tool wraps the
/// existing <see cref="WatermarkRemover.Core.Interfaces.ITextCleaningPipeline"/>,
/// so we test the wiring + result shape here and trust the pipeline's
/// own tests (in <c>WatermarkRemover.Text.Tests</c>) to cover the
/// cleaning correctness.
/// </summary>
public sealed class CleanTextToolTests
{
    [Fact]
    public async Task CleanText_RemovesZwsp_FromCleanedTextBlock()
    {
        // Arrange — wire the real pipeline; the SDK's DI parameter
        // binding will pick this up when an MCP host resolves the tool.
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();
        AppConfig config = AppConfig.Default;

        // ZWSP (U+200B) is the canonical invisible watermark char.
        string input = "Hello\u200BWorld";
        string expected = "HelloWorld";

        // Act
        IEnumerable<ContentBlock> blocks = await CleanTextTool.CleanText(pipeline, config, input);

        // Assert
        TextContentBlock text = blocks.OfType<TextContentBlock>().Single();
        text.Text.Should().Be(expected);
    }

    [Fact]
    public async Task CleanText_OptionalSummaryBlock_OnlyWhenRequested()
    {
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();

        // No summary by default.
        IEnumerable<ContentBlock> without = await CleanTextTool.CleanText(
            pipeline, AppConfig.Default, "hello");
        without.OfType<TextContentBlock>().Should().HaveCount(1);

        // Two blocks when include_removed_summary = true.
        IEnumerable<ContentBlock> with = await CleanTextTool.CleanText(
            pipeline, AppConfig.Default, "hello", include_removed_summary: true);
        TextContentBlock[] withBlocks = with.OfType<TextContentBlock>().ToArray();
        withBlocks.Should().HaveCount(2);

        // The second block is a JSON sidecar; sanity-check it parses.
        using JsonDocument doc = JsonDocument.Parse(withBlocks[1].Text);
        doc.RootElement.TryGetProperty("confidence", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public async Task CleanText_NullText_ThrowsMcpException()
    {
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();

        Func<Task> act = () => CleanTextTool.CleanText(pipeline, AppConfig.Default, text: null!);

        await act.Should().ThrowAsync<ModelContextProtocol.McpException>()
            .WithMessage("*`text` is required*");
    }

    private static ServiceProvider BuildTextHost()
    {
        ServiceCollection services = new();
        services.AddWatermarkRemoverText();
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }
}
