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
/// Unit tests for <see cref="CleanMarkdownTool"/>. The markdown cleaner
/// already has its own test coverage in <c>WatermarkRemover.Text.Tests</c>;
/// these tests verify the MCP tool wiring (return type, summary block,
/// null guard) only.
/// </summary>
public sealed class CleanMarkdownToolTests
{
    [Fact]
    public void CleanMarkdown_StripsFrontmatter_FromCleanedTextBlock()
    {
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();
        AppConfig config = AppConfig.Default;

        string input = "---\ntitle: Test\n---\n# Hello\nWorld\n";
        IEnumerable<ContentBlock> blocks = CleanMarkdownTool.CleanMarkdown(cleaner, config, input);

        TextContentBlock text = blocks.OfType<TextContentBlock>().Single();
        text.Text.Should().NotContain("title: Test");
        text.Text.Should().Contain("Hello");
    }

    [Fact]
    public void CleanMarkdown_OptionalSummaryBlock_OnlyWhenRequested()
    {
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();

        IEnumerable<ContentBlock> without = CleanMarkdownTool.CleanMarkdown(cleaner, AppConfig.Default, "# hi");
        without.OfType<TextContentBlock>().Should().HaveCount(1);

        IEnumerable<ContentBlock> with = CleanMarkdownTool.CleanMarkdown(
            cleaner, AppConfig.Default, "# hi", include_removed_summary: true);
        TextContentBlock[] withBlocks = with.OfType<TextContentBlock>().ToArray();
        withBlocks.Should().HaveCount(2);

        using JsonDocument doc = JsonDocument.Parse(withBlocks[1].Text);
        doc.RootElement.TryGetProperty("codeBlocksFound", out JsonElement _).Should().BeTrue();
        doc.RootElement.TryGetProperty("codeBlocksPreserved", out JsonElement _).Should().BeTrue();
        doc.RootElement.TryGetProperty("frontmatterRemoved", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public void CleanMarkdown_NullMarkdown_ThrowsMcpException()
    {
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();

        Action act = () => CleanMarkdownTool.CleanMarkdown(cleaner, AppConfig.Default, markdown: null!);

        act.Should().Throw<ModelContextProtocol.McpException>()
            .WithMessage("*`markdown` is required*");
    }

    private static ServiceProvider BuildTextHost()
    {
        ServiceCollection services = new();
        services.AddWatermarkRemoverText();
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }
}
