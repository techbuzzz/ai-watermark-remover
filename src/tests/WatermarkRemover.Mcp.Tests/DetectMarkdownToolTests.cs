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
/// Unit tests for <see cref="DetectMarkdownTool"/>.
/// </summary>
public sealed class DetectMarkdownToolTests
{
    [Fact]
    public void DetectMarkdown_AiSignaturePresent_ReturnsArrayWithEntries()
    {
        // The markdown detect path reports AI tool signatures,
        // boilerplate, invisible separators, and invisible-in-code.
        // It deliberately does NOT report frontmatter (the cleaner
        // owns that, see MarkdownCleaner.Detect for the list).
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();

        string input = "# body\n\n🤖 Generated with ChatGPT\n";

        TextContentBlock result = DetectMarkdownTool.DetectMarkdown(cleaner, input);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        JsonElement.ArrayEnumerator arr = doc.RootElement.EnumerateArray();
        List<JsonElement> elements = arr.ToList();
        elements.Should().NotBeEmpty();

        // AiArtifact record shape: Type / Description / Line / Column.
        JsonElement first = elements[0];
        first.TryGetProperty("Type", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Description", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Line", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Column", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public void DetectMarkdown_NoFrontmatter_ReturnsEmptyArray()
    {
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();

        TextContentBlock result = DetectMarkdownTool.DetectMarkdown(cleaner, "# plain\n");

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void DetectMarkdown_NullInput_ThrowsMcpException()
    {
        ServiceProvider sp = BuildTextHost();
        var cleaner = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>();

        Action act = () => DetectMarkdownTool.DetectMarkdown(cleaner, markdown: null!);

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
