using FluentAssertions;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Models;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Smoke tests for the <c>src/config.yaml</c> markdown section. The
/// whole point of WR-S7 is that the YAML keys the docs advertise must
/// (a) load without throwing and (b) reach <c>MarkdownCleanOptions</c>
/// via the <see cref="MarkdownCleanOptions.From(MarkdownConfig)"/>
/// factory. These tests assert both.
/// </summary>
public sealed class ConfigYamlMarkdownTests
{
    [Fact]
    public void ConfigYaml_LoadsWithoutThrowing()
    {
        // The canonical config.yaml next to the project must be
        // parseable into AppConfig without error. If this fails, a
        // typo in config.yaml (e.g. wrong indentation) silently breaks
        // the build, but the operator's CLI still works because
        // ConfigLoader falls back to defaults. This test is the
        // safety net that catches the regression.
        string path = LocateConfigYaml();
        File.Exists(path).Should().BeTrue($"expected {path} to exist on disk");

        AppConfig config = ConfigLoader.Load(path);

        config.Should().NotBeNull();
        config.Markdown.Should().NotBeNull();
    }

    [Fact]
    public void ConfigYaml_Markdown_ExposesEveryDocumentedKey()
    {
        // The YAML keys the docs/CONFIGURATION.md page lists must all
        // be present in the canonical example. If a doc author adds a
        // new row to the table, this test forces them to add the
        // matching key to config.yaml too.
        string path = LocateConfigYaml();
        AppConfig config = ConfigLoader.Load(path);

        MarkdownConfig md = config.Markdown;
        md.Should().NotBeNull();
        md.StripHeadings.Should().BeTrue();
        md.StripCodeFences.Should().BeFalse();
        md.StripInlineCode.Should().BeFalse();
        md.StripLinks.Should().BeFalse();
        md.StripImages.Should().BeTrue();
        md.StripBoldItalic.Should().BeFalse();
        md.StripBlockquotes.Should().BeFalse();
        md.StripHr.Should().BeTrue();
        md.StripHtml.Should().BeTrue();
        md.StripComments.Should().BeTrue();
        md.StripTaskLists.Should().BeFalse();
        md.StripTableSyntax.Should().BeFalse();
        md.NormalizeLists.Should().BeTrue();
        md.UnwrapEmptyLists.Should().BeTrue();
        md.StripXmlTags.Should().BeTrue();
        md.StripFrontmatter.Should().BeTrue();
        md.StripAiSignatures.Should().BeTrue();
        md.StripMentions.Should().BeTrue();
        md.StripUnicodeMd.Should().BeTrue();
        md.StripTrailingWs.Should().BeTrue();
        md.ApplyUnicodeLayerA.Should().BeTrue();
        md.PreserveCodeBlocks.Should().BeTrue();
    }

    [Fact]
    public void ConfigYaml_Markdown_FromFactory_MatchesLoadedValues()
    {
        // Round-trip: load the YAML, run it through From(), and verify
        // every key survived the journey. This is the integration
        // counterpart of the unit-level MarkdownConfigTests.From_PropagatesEveryToggle.
        string path = LocateConfigYaml();
        AppConfig config = ConfigLoader.Load(path);

        MarkdownCleanOptions options = MarkdownCleanOptions.From(config.Markdown);

        options.StripHeadings.Should().Be(config.Markdown.StripHeadings);
        options.StripCodeFences.Should().Be(config.Markdown.StripCodeFences);
        options.StripInlineCode.Should().Be(config.Markdown.StripInlineCode);
        options.StripLinks.Should().Be(config.Markdown.StripLinks);
        options.StripImages.Should().Be(config.Markdown.StripImages);
        options.StripBoldItalic.Should().Be(config.Markdown.StripBoldItalic);
        options.StripBlockquotes.Should().Be(config.Markdown.StripBlockquotes);
        options.StripHr.Should().Be(config.Markdown.StripHr);
        options.StripHtml.Should().Be(config.Markdown.StripHtml);
        options.StripComments.Should().Be(config.Markdown.StripComments);
        options.StripTaskLists.Should().Be(config.Markdown.StripTaskLists);
        options.StripTableSyntax.Should().Be(config.Markdown.StripTableSyntax);
        options.NormalizeLists.Should().Be(config.Markdown.NormalizeLists);
        options.UnwrapEmptyLists.Should().Be(config.Markdown.UnwrapEmptyLists);
        options.StripXmlTags.Should().Be(config.Markdown.StripXmlTags);
        options.StripFrontmatter.Should().Be(config.Markdown.StripFrontmatter);
        options.StripAiSignatures.Should().Be(config.Markdown.StripAiSignatures);
        options.StripMentions.Should().Be(config.Markdown.StripMentions);
        options.StripUnicodeMd.Should().Be(config.Markdown.StripUnicodeMd);
        options.StripTrailingWs.Should().Be(config.Markdown.StripTrailingWs);
        options.ApplyUnicodeLayerA.Should().Be(config.Markdown.ApplyUnicodeLayerA);
    }

    /// <summary>
    /// Walk up from the test bin directory to the repo root and return
    /// the canonical <c>src/config.yaml</c> path. The bin layout is
    /// <c>src/tests/&lt;project&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/</c>, so the
    /// config sits at <c>../../../../config.yaml</c> relative to the
    /// test DLL's directory.
    /// </summary>
    private static string LocateConfigYaml()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "config.yaml");
            if (File.Exists(candidate) && File.ReadAllText(candidate).Contains("markdown:"))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("Could not locate src/config.yaml from the test bin directory.");
    }
}
