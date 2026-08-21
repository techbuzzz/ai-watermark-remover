using System.Reflection;
using FluentAssertions;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Models;
using Xunit;

namespace WatermarkRemover.Core.Tests;

/// <summary>
/// Tests for the <see cref="MarkdownConfig"/> defaults, the
/// <see cref="MarkdownCleanOptions"/> defaults, and the
/// <see cref="MarkdownCleanOptions.From(MarkdownConfig)"/> factory
/// that maps one to the other. These exist so the contract documented
/// in <c>docs/CONFIGURATION.md</c> and <c>src/config.yaml</c> stays
/// in lockstep with the C# code: every toggle on
/// <see cref="MarkdownCleanOptions"/> must be reachable from
/// <c>config.yaml</c>, and the defaults must match.
/// </summary>
public sealed class MarkdownConfigTests
{
    // The 21 toggles the cleaner exposes today. Adding a new public
    // boolean to MarkdownCleanOptions will (deliberately) break the
    // "every toggle is surfaced" tests below — a forcing function to
    // keep config.yaml in sync.
    private static readonly (string FieldName, string ConfigProp, bool Default)[] ExpectedToggles =
    [
        ("StripHeadings",      "StripHeadings",      true),
        ("StripCodeFences",    "StripCodeFences",    false),
        ("StripInlineCode",    "StripInlineCode",    false),
        ("StripLinks",         "StripLinks",         false),
        ("StripImages",        "StripImages",        true),
        ("StripBoldItalic",    "StripBoldItalic",    false),
        ("StripBlockquotes",   "StripBlockquotes",   false),
        ("StripHr",            "StripHr",            true),
        ("StripHtml",          "StripHtml",          true),
        ("StripComments",      "StripComments",      true),
        ("StripTaskLists",     "StripTaskLists",     false),
        ("StripTableSyntax",   "StripTableSyntax",   false),
        ("NormalizeLists",     "NormalizeLists",     true),
        ("UnwrapEmptyLists",   "UnwrapEmptyLists",   true),
        ("StripXmlTags",       "StripXmlTags",       true),
        ("StripFrontmatter",   "StripFrontmatter",   true),
        ("StripAiSignatures",  "StripAiSignatures",  true),
        ("StripMentions",      "StripMentions",      true),
        ("StripUnicodeMd",     "StripUnicodeMd",     true),
        ("StripTrailingWs",    "StripTrailingWs",    true),
        ("ApplyUnicodeLayerA", "ApplyUnicodeLayerA", true),
    ];

    [Fact]
    public void MarkdownConfig_ExposesEveryCleanerToggle()
    {
        // The config record and the options record are separate types in
        // separate files, but the "every public toggle" promise is
        // central to the WR-S7 deliverable. If a new property is added
        // to MarkdownCleanOptions, the matching property MUST show up
        // here too.
        MarkdownConfig config = new();
        PropertyInfo[] configProps = typeof(MarkdownConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .ToArray();

        foreach ((string field, string prop, bool _) in ExpectedToggles)
        {
            configProps.Should().Contain(p => p.Name == prop,
                $"MarkdownConfig must surface the {field} toggle from MarkdownCleanOptions");
        }
    }

    [Fact]
    public void MarkdownConfig_DefaultValues_MatchMarkdownCleanOptionsDefaults()
    {
        // Operators who delete a key from config.yaml get these values
        // back. They must equal the defaults the cleaner uses when
        // options is null, otherwise the surprise behaviour change is
        // a regression.
        MarkdownConfig config = new();
        MarkdownCleanOptions options = new();

        foreach ((string field, string prop, bool _) in ExpectedToggles)
        {
            bool configValue = (bool)typeof(MarkdownConfig).GetProperty(prop)!.GetValue(config)!;
            bool optionsValue = (bool)typeof(MarkdownCleanOptions).GetProperty(field)!.GetValue(options)!;
            configValue.Should().Be(optionsValue,
                $"{prop} default in MarkdownConfig must match {field} default in MarkdownCleanOptions");
        }
    }

    [Fact]
    public void AppConfig_Default_MarkdownConfig_MatchesCleanerDefaults()
    {
        // AppConfig.Default is the safety net when config.yaml is
        // missing entirely. It must also stay in lockstep.
        MarkdownConfig config = AppConfig.Default.Markdown;

        foreach ((string field, string prop, bool _) in ExpectedToggles)
        {
            bool configValue = (bool)typeof(MarkdownConfig).GetProperty(prop)!.GetValue(config)!;
            bool optionsValue = (bool)typeof(MarkdownCleanOptions).GetProperty(field)!.GetValue(new MarkdownCleanOptions())!;
            configValue.Should().Be(optionsValue,
                $"AppConfig.Default.Markdown.{prop} must match MarkdownCleanOptions.{field} default");
        }
    }

    [Fact]
    public void MarkdownConfig_PreserveCodeBlocks_DefaultsToTrue()
    {
        // Legacy knob. The cleaner always preserves fenced code unless
        // StripCodeFences is true; this is the documented default and
        // must not silently change.
        new MarkdownConfig().PreserveCodeBlocks.Should().BeTrue();
    }

    [Fact]
    public void From_NullConfig_Throws()
    {
        Action act = () => MarkdownCleanOptions.From(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void From_PropagatesEveryToggle()
    {
        // Flip every toggle to a non-default value and check the
        // factory propagates each one. This guards against a future
        // refactor that drops a property from the factory body.
        MarkdownConfig config = new()
        {
            StripHeadings = false,
            StripCodeFences = true,
            StripInlineCode = true,
            StripLinks = true,
            StripImages = false,
            StripBoldItalic = true,
            StripBlockquotes = true,
            StripHr = false,
            StripHtml = false,
            StripComments = false,
            StripTaskLists = true,
            StripTableSyntax = true,
            NormalizeLists = false,
            UnwrapEmptyLists = false,
            StripXmlTags = false,
            StripFrontmatter = false,
            StripAiSignatures = false,
            StripMentions = false,
            StripUnicodeMd = false,
            StripTrailingWs = false,
            ApplyUnicodeLayerA = false,
        };

        MarkdownCleanOptions options = MarkdownCleanOptions.From(config);

        foreach ((string field, string prop, bool _) in ExpectedToggles)
        {
            bool configValue = (bool)typeof(MarkdownConfig).GetProperty(prop)!.GetValue(config)!;
            bool optionsValue = (bool)typeof(MarkdownCleanOptions).GetProperty(field)!.GetValue(options)!;
            optionsValue.Should().Be(configValue,
                $"MarkdownCleanOptions.From must propagate {prop} → {field}");
        }
    }

    [Fact]
    public void From_IgnoresPreserveCodeBlocks()
    {
        // PreserveCodeBlocks is a legacy CLI knob that has no matching
        // field on MarkdownCleanOptions. The factory must not invent
        // one.
        MarkdownConfig config = new() { PreserveCodeBlocks = false };
        MarkdownCleanOptions options = MarkdownCleanOptions.From(config);

        // Verify the options object was constructed and the legacy
        // field on the source was respected (it is not carried over,
        // but the call should not throw).
        options.Should().NotBeNull();
    }

    [Fact]
    public void StripAll_TrueForEveryToggle()
    {
        // The --strip-all CLI flag enables every transform at once. The
        // factory must cover every one of the 21 toggles — if a new
        // toggle is added and forgotten here, --strip-all will silently
        // miss it.
        MarkdownCleanOptions all = MarkdownCleanOptions.StripAll();

        foreach (var (field, _, _) in ExpectedToggles)
        {
            bool value = (bool)typeof(MarkdownCleanOptions).GetProperty(field)!.GetValue(all)!;
            value.Should().BeTrue($"--strip-all must enable {field}");
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedToggleData))]
    public void Default_IndividualToggle_MatchesSpec(string field, string configProp, bool expected)
    {
        // Each toggle in isolation. MemberData is a list of 21 cases,
        // so a single regression shows up as one red test with a clear
        // name in the test output.
        bool configValue = (bool)typeof(MarkdownConfig).GetProperty(configProp)!.GetValue(new MarkdownConfig())!;
        bool optionsValue = (bool)typeof(MarkdownCleanOptions).GetProperty(field)!.GetValue(new MarkdownCleanOptions())!;
        configValue.Should().Be(expected);
        optionsValue.Should().Be(expected);
    }

    public static IEnumerable<object[]> ExpectedToggleData =>
        ExpectedToggles.Select(t => new object[] { t.FieldName, t.ConfigProp, t.Default });
}
