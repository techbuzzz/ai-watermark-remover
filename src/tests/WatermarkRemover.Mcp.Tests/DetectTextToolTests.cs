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
/// Unit tests for <see cref="DetectTextTool"/>. Verifies the JSON
/// sidecar shape that the tool returns.
/// </summary>
public sealed class DetectTextToolTests
{
    [Fact]
    public void DetectText_ReturnsJsonArray_SingleTextBlock()
    {
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();

        TextContentBlock result = DetectTextTool.DetectText(pipeline, "plain text without watermarks");

        // The detector returns IReadOnlyList<WatermarkMatch>; the tool
        // serialises it to JSON inside a single TextContentBlock.
        using JsonDocument doc = JsonDocument.Parse(result.Text);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void DetectText_FindsZeroWidthSequence_AsJsonElement()
    {
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();

        // The Claude detector flags zero-width runs of length >= 2
        // (see ClaudeWatermarkDetector in WatermarkRemover.Text).
        // Two consecutive ZWSP characters between visible glyphs are
        // a deterministic signature.
        string input = "Hello\u200B\u200CWorld";

        TextContentBlock result = DetectTextTool.DetectText(pipeline, input);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        JsonElement.ArrayEnumerator enumerator = doc.RootElement.EnumerateArray();
        List<JsonElement> elements = enumerator.ToList();

        elements.Should().NotBeEmpty();
        JsonElement first = elements[0];
        first.TryGetProperty("Vendor", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Pattern", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Position", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Length", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Confidence", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public void DetectText_NullText_ThrowsMcpException()
    {
        ServiceProvider sp = BuildTextHost();
        var pipeline = sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>();

        Action act = () => DetectTextTool.DetectText(pipeline, text: null!);

        act.Should().Throw<ModelContextProtocol.McpException>()
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
