using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Static-structural tests for the VS Code extension shipped under
/// <c>vscode/watermark-remover/</c>. The package must conform to the
/// VS Code extension manifest spec — <c>package.json</c> with all
/// required fields, every <c>contributes.commands</c> entry wired to
/// a <c>contributes.menus</c> slot, <c>activationEvents</c> covering
/// every command, <c>engines.vscode</c> pinning the minimum supported
/// version, and the bundled <c>skills/</c> folder (master + five
/// per-format) being present. The behavioural tests for the actual
/// command implementations live in the extension's own Node suite
/// under <c>vscode/watermark-remover/test/extension.test.js</c>; this
/// fixture covers the static contract that the parent repo owns.
/// </summary>
/// <remarks>
/// The reference is the
/// <a href="https://code.visualstudio.com/api/references/extension-manifest">VS
/// Code extension manifest</a> docs (and the Marketplace publisher
/// requirements). The test here only checks what the runtime can
/// verify statically from the manifest — actual extension behaviour
/// (right-click menus, command execution) requires running VS Code
/// itself, which is out of scope for the parent-repo test suite.
/// </remarks>
public sealed class VsCodeExtensionTests
{
    private const string ExtensionRelativeDir = "vscode/watermark-remover";
    private const string PackageJsonPath = "package.json";
    private const string TsConfigPath = "tsconfig.json";
    private const string ReadmePath = "README.md";
    private const string ChangelogPath = "CHANGELOG.md";
    private const string VsCodeIgnorePath = ".vscodeignore";
    private const string GitIgnorePath = ".gitignore";
    private const string ExtensionTsPath = "src/extension.ts";
    private const string SkillsDir = "skills";
    private const string TestDir = "test";

    private static readonly string[] RequiredTopLevelFields =
    {
        "name",
        "displayName",
        "description",
        "version",
        "publisher",
        "engines",
        "categories",
        "activationEvents",
        "main",
        "contributes",
        "scripts",
    };

    private static readonly string[] RequiredCommandIds =
    {
        "watermarkremover.cleanText",
        "watermarkremover.cleanFile",
        "watermarkremover.detectText",
    };

    private static readonly string[] RequiredSettings =
    {
        "watermarkremover.binaryPath",
        "watermarkremover.preferMcp",
        "watermarkremover.statistical",
        "watermarkremover.showNotifications",
    };

    private static readonly string[] RequiredSkillFolders =
    {
        "watermark-remover",
        "clean-text",
        "clean-markdown",
        "clean-file",
        "clean-image",
        "detect",
    };

    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.Compiled);

    private static readonly Regex SupportedVsCodeRangePattern = new(
        @"^\^1\.(8[5-9]|9[0-9])\.0$",
        RegexOptions.Compiled);

    // ---- file presence ----------------------------------------------------

    [Fact]
    public void ExtensionDirectory_IsPresent()
    {
        Directory.Exists(LocateExtensionDir())
            .Should()
            .BeTrue($"expected VS Code extension folder at {LocateExtensionDir()}");
    }

    [Theory]
    [InlineData(PackageJsonPath)]
    [InlineData(TsConfigPath)]
    [InlineData(ReadmePath)]
    [InlineData(ChangelogPath)]
    [InlineData(VsCodeIgnorePath)]
    [InlineData(GitIgnorePath)]
    [InlineData(ExtensionTsPath)]
    public void RequiredFile_IsPresent(string relative)
    {
        string path = Path.Combine(LocateExtensionDir(), relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"required file {relative} must be shipped");
    }

    [Fact]
    public void SkillsDirectory_IsPresent()
    {
        string path = Path.Combine(LocateExtensionDir(), SkillsDir);
        Directory.Exists(path).Should().BeTrue("the extension must ship a skills/ directory");
    }

    [Fact]
    public void TestDirectory_ContainsNodeTestSuite()
    {
        string testDir = Path.Combine(LocateExtensionDir(), TestDir);
        Directory.Exists(testDir).Should().BeTrue("the extension must ship a Node test suite");
        Directory.GetFiles(testDir, "*.test.js")
            .Should()
            .NotBeEmpty("at least one *.test.js file must be present so `npm test` is non-trivial");
    }

    [Theory]
    [InlineData("watermark-remover")]
    [InlineData("clean-text")]
    [InlineData("clean-markdown")]
    [InlineData("clean-file")]
    [InlineData("clean-image")]
    [InlineData("detect")]
    public void RequiredSkillFolder_IsPresent(string skillName)
    {
        string skillPath = Path.Combine(
            LocateExtensionDir(),
            SkillsDir,
            skillName,
            "SKILL.md");
        File.Exists(skillPath).Should().BeTrue($"skills/{skillName}/SKILL.md must be shipped");
    }

    // ---- package.json shape -----------------------------------------------

    [Fact]
    public void PackageJson_IsValidJson()
    {
        string raw = File.ReadAllText(Locate(PackageJsonPath));
        Action act = () => JsonDocument.Parse(raw);
        act.Should().NotThrow($"the extension's package.json at {Locate(PackageJsonPath)} must be valid JSON");
    }

    [Fact]
    public void PackageJson_HasAllRequiredTopLevelFields()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement root = doc.RootElement;

        foreach (string field in RequiredTopLevelFields)
        {
            root.TryGetProperty(field, out _)
                .Should()
                .BeTrue($"package.json field '{field}' is required by the VS Code extension manifest spec");
        }
    }

    [Fact]
    public void PackageJson_NameAndPublisher_AreStable()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        doc.RootElement.GetProperty("name").GetString().Should().Be("watermark-remover");
        doc.RootElement.GetProperty("publisher").GetString().Should().Be("techbuzzz");
    }

    [Fact]
    public void PackageJson_Version_IsSemVer()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        string version = doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
        SemVerPattern.IsMatch(version)
            .Should()
            .BeTrue($"version '{version}' must be SemVer");
    }

    [Fact]
    public void PackageJson_EnginesVscode_RequiresAtLeast1_85()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        string vscodeRange = doc.RootElement.GetProperty("engines").GetProperty("vscode").GetString() ?? string.Empty;
        SupportedVsCodeRangePattern.IsMatch(vscodeRange)
            .Should()
            .BeTrue($"engines.vscode '{vscodeRange}' must be ^1.85.0 or later (matches the manifest spec version this extension targets)");
    }

    [Fact]
    public void PackageJson_EnginesNode_Requires18OrLater()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        string nodeRange = doc.RootElement.GetProperty("engines").GetProperty("node").GetString() ?? string.Empty;
        nodeRange.Should().MatchRegex(
            @">=1[89]\.|>=2[0-9]\.",
            "Node 18+ is required (uses node:test, fetch, etc.)");
    }

    [Fact]
    public void PackageJson_ActivationEvents_CoverAllCommands()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement events = doc.RootElement.GetProperty("activationEvents");
        events.ValueKind.Should().Be(JsonValueKind.Array);
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (JsonElement e in events.EnumerateArray())
        {
            string? s = e.GetString();
            if (s is not null)
            {
                set.Add(s);
            }
        }

        foreach (string command in RequiredCommandIds)
        {
            set.Should()
                .Contain($"onCommand:{command}", $"activationEvents must include onCommand:{command}");
        }
    }

    [Fact]
    public void PackageJson_ContributesCommands_RegistersAllThreeCommands()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement commands = doc.RootElement.GetProperty("contributes").GetProperty("commands");
        commands.ValueKind.Should().Be(JsonValueKind.Array);

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement c in commands.EnumerateArray())
        {
            string id = c.GetProperty("command").GetString() ?? string.Empty;
            ids.Add(id);
            string title = c.GetProperty("title").GetString() ?? string.Empty;
            title.Trim().Should().NotBeEmpty($"command {id} must have a non-empty title");
            string? category = c.TryGetProperty("category", out JsonElement cat) ? cat.GetString() : null;
            category.Should().Be("WatermarkRemover", $"command {id} must use the WatermarkRemover category");
        }

        foreach (string required in RequiredCommandIds)
        {
            ids.Should().Contain(required, $"contributes.commands must include {required}");
        }
    }

    [Theory]
    [InlineData("editor/context")]
    [InlineData("editor/context/contextual")]
    [InlineData("commandPalette")]
    public void PackageJson_EditorMenus_IncludeTextCommands(string menuName)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement menus = doc.RootElement.GetProperty("contributes").GetProperty("menus");
        menus.TryGetProperty(menuName, out JsonElement menu).Should().BeTrue($"menus.{menuName} must exist");
        HashSet<string> ids = CollectMenuCommandIds(menu);
        ids.Should().Contain("watermarkremover.cleanText", $"menus.{menuName} must surface the cleanText command");
        ids.Should().Contain("watermarkremover.detectText", $"menus.{menuName} must surface the detectText command");
    }

    [Theory]
    [InlineData("explorer/context")]
    [InlineData("explorer/context/contextual")]
    public void PackageJson_ExplorerMenus_IncludeFileCommand(string menuName)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement menus = doc.RootElement.GetProperty("contributes").GetProperty("menus");
        menus.TryGetProperty(menuName, out JsonElement menu).Should().BeTrue($"menus.{menuName} must exist");
        HashSet<string> ids = CollectMenuCommandIds(menu);
        ids.Should().Contain("watermarkremover.cleanFile", $"menus.{menuName} must surface the cleanFile command");
    }

    [Fact]
    public void PackageJson_ContributesConfiguration_DefinesAllSettings()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement properties = doc.RootElement.GetProperty("contributes").GetProperty("configuration").GetProperty("properties");
        foreach (string key in RequiredSettings)
        {
            properties.TryGetProperty(key, out _)
                .Should()
                .BeTrue($"configuration.properties.{key} is required");
        }
    }

    [Fact]
    public void PackageJson_Scripts_BuildAndTestMatchConvention()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement scripts = doc.RootElement.GetProperty("scripts");
        scripts.GetProperty("build").GetString().Should().Be("tsc -p .");
        scripts.GetProperty("vscode:prepublish").GetString().Should().Be("npm run build");
        string? test = scripts.GetProperty("test").GetString();
        test.Should().NotBeNullOrEmpty();
        test.Should().StartWith("node --test ", "the test script must invoke the node test runner");
    }

    [Fact]
    public void PackageJson_DevDependencies_IncludeTypeScriptAndTypes()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement devDeps = doc.RootElement.GetProperty("devDependencies");
        devDeps.TryGetProperty("typescript", out _).Should().BeTrue("typescript devDependency is required");
        devDeps.TryGetProperty("@types/vscode", out _).Should().BeTrue("@types/vscode devDependency is required");
        devDeps.TryGetProperty("@types/node", out _).Should().BeTrue("@types/node devDependency is required");
    }

    [Fact]
    public void PackageJson_Repository_DirectoryPointsAtExtensionFolder()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement repo = doc.RootElement.GetProperty("repository");
        repo.GetProperty("type").GetString().Should().Be("git");
        repo.GetProperty("url").GetString().Should().Contain("techbuzzz/ai-watermark-remover");
        repo.GetProperty("directory").GetString().Should().Be(ExtensionRelativeDir);
    }

    // ---- tsconfig.json -----------------------------------------------------

    [Fact]
    public void TsConfig_IsValidJson()
    {
        string raw = File.ReadAllText(Locate(TsConfigPath));
        Action act = () => JsonDocument.Parse(raw);
        act.Should().NotThrow($"the extension's tsconfig.json at {Locate(TsConfigPath)} must be valid JSON");
    }

    [Fact]
    public void TsConfig_TargetsEs2022WithStrictMode()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(TsConfigPath)));
        JsonElement opts = doc.RootElement.GetProperty("compilerOptions");
        opts.GetProperty("target").GetString().Should().Be("ES2022");
        opts.GetProperty("strict").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TsConfig_IncludesSourceFolder()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(TsConfigPath)));
        JsonElement include = doc.RootElement.GetProperty("include");
        include.ValueKind.Should().Be(JsonValueKind.Array);
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (JsonElement e in include.EnumerateArray())
        {
            string? s = e.GetString();
            if (s is not null)
            {
                set.Add(s);
            }
        }

        set.Should().Contain("src/**/*", "tsconfig include must cover src/**/* so the build picks up extension.ts");
    }

    // ---- extension.ts content ---------------------------------------------

    [Fact]
    public void ExtensionTs_RegistersAllThreeCommands()
    {
        string raw = File.ReadAllText(Locate(ExtensionTsPath));
        foreach (string command in RequiredCommandIds)
        {
            raw.Should().Contain(command, $"src/extension.ts must register the command {command}");
        }
    }

    [Fact]
    public void ExtensionTs_ImportsChildProcess()
    {
        string raw = File.ReadAllText(Locate(ExtensionTsPath));
        (raw.Contains("'node:child_process'") || raw.Contains("'child_process'"))
            .Should()
            .BeTrue("src/extension.ts must import child_process to spawn the CLI binary");
    }

    [Fact]
    public void ExtensionTs_InvokesWatermarkRemoverCliSubcommands()
    {
        string raw = File.ReadAllText(Locate(ExtensionTsPath));
        // Each of the three commands must shell out to the matching CLI subcommand.
        raw.Should().Contain("clean-text", "the cleanText command must invoke 'watermarkremover clean-text'");
        raw.Should().Contain("clean-file", "the cleanFile command must invoke 'watermarkremover clean-file'");
        raw.Should().Contain("detect-text", "the detectText command must invoke 'watermarkremover detect-text'");
    }

    // ---- README.md / CHANGELOG.md / .vscodeignore -------------------------

    [Fact]
    public void Readme_IsSubstantialMarketplaceListing()
    {
        string raw = File.ReadAllText(Locate(ReadmePath));
        raw.Length.Should().BeGreaterThan(2000, "the README must be a substantial marketplace listing");
        raw.Should().Contain("Requirements", "the README must describe the binary requirement");
        raw.Should().Contain("Extension settings", "the README must document the settings");
        raw.Should().Contain("VS Code", "the README must mention VS Code in the body");
    }

    [Fact]
    public void Changelog_HasUnreleasedEntry()
    {
        string raw = File.ReadAllText(Locate(ChangelogPath));
        raw.Should().Contain("Unreleased", "the CHANGELOG must have an Unreleased section");
        raw.Should().Contain("Added", "the CHANGELOG must have an Added subsection under Unreleased");
    }

    [Fact]
    public void VsCodeIgnore_ExcludesSourceTypeScript()
    {
        string raw = File.ReadAllText(Locate(VsCodeIgnorePath));
        raw.Should().Contain("src/", ".vscodeignore must exclude the src/ directory (compiled via vscode:prepublish)");
        raw.Should().Contain("tsconfig.json", ".vscodeignore must exclude tsconfig.json");
    }

    // ---- master skill (YAML frontmatter) ----------------------------------

    [Fact]
    public void MasterSkill_FrontmatterMentionsVscodeCompatibility()
    {
        string raw = File.ReadAllText(Path.Combine(LocateExtensionDir(), SkillsDir, "watermark-remover", "SKILL.md"));
        raw.Should().StartWith("---", "the master skill must start with YAML frontmatter");
        Regex.IsMatch(
                raw,
                @"^compatibility:\s*.+vscode.+$",
                RegexOptions.Multiline)
            .Should()
            .BeTrue("the master skill's compatibility line must include 'vscode' so agents that read SKILL.md learn the extension is available");
    }

    // ---- docs/MCP.md and docs/VS-CODE.md ----------------------------------

    [Fact]
    public void McpDoc_HasVsCodeSection()
    {
        // We walk up from the extension dir to find the repo root, then
        // assert docs/MCP.md has the VS Code install section.
        string mcpDoc = Path.Combine(LocateRepoRoot(), "docs", "MCP.md");
        File.Exists(mcpDoc).Should().BeTrue("docs/MCP.md must be present in the parent repo");
        string raw = File.ReadAllText(mcpDoc);
        Regex.IsMatch(
                raw,
                @"^#{2,4}\s+VS Code\b",
                RegexOptions.Multiline)
            .Should()
            .BeTrue("docs/MCP.md must contain a 'VS Code' install section");
        Regex.IsMatch(
                raw,
                @"vscode/watermark-remover",
                RegexOptions.None)
            .Should()
            .BeTrue("docs/MCP.md must reference the vscode/watermark-remover/ directory");
    }

    [Fact]
    public void VsCodeDoc_IsPresentAndHasExpectedSections()
    {
        string vsCodeDoc = Path.Combine(LocateRepoRoot(), "docs", "VS-CODE.md");
        File.Exists(vsCodeDoc).Should().BeTrue("docs/VS-CODE.md must be present");
        string raw = File.ReadAllText(vsCodeDoc);
        raw.Should().Contain("Install", "docs/VS-CODE.md must have an Install section");
        raw.Should().Contain("Usage", "docs/VS-CODE.md must have a Usage section");
        raw.Should().Contain("Extension settings", "docs/VS-CODE.md must document the settings");
        raw.Should().Contain("Troubleshooting", "docs/VS-CODE.md must have a Troubleshooting section");
    }

    [Fact]
    public void Readme_HasVsCodeCallout()
    {
        string readme = Path.Combine(LocateRepoRoot(), "README.md");
        File.Exists(readme).Should().BeTrue();
        string raw = File.ReadAllText(readme);
        raw.Should().Contain("VS Code users get a first-party extension", "the README must have the VS Code callout");
        raw.Should().Contain("docs/VS-CODE.md", "the README must link to docs/VS-CODE.md");
    }

    // ---- helpers ---------------------------------------------------------

    private static HashSet<string> CollectMenuCommandIds(JsonElement menu)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (menu.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (JsonElement entry in menu.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("command", out JsonElement cmd)
                && cmd.ValueKind == JsonValueKind.String)
            {
                string? id = cmd.GetString();
                if (id is not null)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static string LocateExtensionDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, ExtensionRelativeDir);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{ExtensionRelativeDir}' walking up from {AppContext.BaseDirectory}.");
    }

    private static string LocateRepoRoot()
    {
        // The .sln / .slnx live under src/, not at the repo root, so the
        // first directory we hit that *contains* an .sln is actually src/,
        // not the repo root. The actual repo root is the **parent** of any
        // directory that contains both src/ and docs/. So we walk up until
        // we find a directory that has a docs/ subfolder, then return that.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "docs"))
                && Directory.Exists(Path.Combine(dir, "src"))
                && File.Exists(Path.Combine(dir, "src", "WatermarkRemover.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the repo root (no docs/ + src/ with WatermarkRemover.sln) walking up from {AppContext.BaseDirectory}.");
    }

    private static string Locate(string relative)
    {
        return Path.Combine(LocateExtensionDir(), relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
