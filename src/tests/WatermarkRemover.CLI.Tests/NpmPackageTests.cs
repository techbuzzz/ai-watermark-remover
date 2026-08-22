using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Static-structural tests for the <c>@watermarkremover/mcp</c> npm
/// package shipped under <c>npm/watermarkremover-mcp/</c>. The dynamic
/// behaviour (platform detection, ZIP extraction, install flow) is
/// covered by the package's own Node test suite under
/// <c>npm/watermarkremover-mcp/test/</c>; this fixture only checks
/// the contract that the parent repository guarantees to npm
/// consumers.
/// </summary>
/// <remarks>
/// The acceptance gate from WR-S18 includes "npx @watermarkremover/mcp
/// starts the MCP server over stdio" — the dynamic half of that is
/// covered by the package's own tests + the JSON-RPC integration
/// tests in <c>WatermarkRemover.Mcp.Tests</c>. This fixture covers
/// the "the package exists, is well-formed, and is wireable" half
/// that the parent repo owns.
/// </remarks>
public sealed class NpmPackageTests
{
    private const string PackageRelativeDir = "npm/watermarkremover-mcp";
    private const string PackageJsonPath = "package.json";
    private const string IndexJsPath = "index.js";
    private const string PostinstallJsPath = "postinstall.js";
    private const string LibDir = "lib";
    private const string BinaryJsPath = "lib/binary.js";
    private const string InstallJsPath = "lib/install.js";
    private const string TestDir = "test";
    private const string ReadmePath = "README.md";

    private static readonly string[] RequiredEntries =
    {
        PackageJsonPath,
        IndexJsPath,
        PostinstallJsPath,
        $"{LibDir}/binary.js",
        $"{LibDir}/install.js",
        ReadmePath,
    };

    private static readonly string[] SupportedRids =
    {
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "win-x64",
    };

    // ---- file presence --------------------------------------------------

    [Fact]
    public void PackageDirectory_IsPresent()
    {
        LocatePackageDir().Should().NotBeNullOrWhiteSpace();
        Directory.Exists(LocatePackageDir())
            .Should()
            .BeTrue("the @watermarkremover/mcp package folder must exist at the repo root");
    }

    [Theory]
    [InlineData(PackageJsonPath)]
    [InlineData(IndexJsPath)]
    [InlineData(PostinstallJsPath)]
    [InlineData(BinaryJsPath)]
    [InlineData(InstallJsPath)]
    [InlineData(ReadmePath)]
    public void RequiredFile_IsPresent(string relative)
    {
        string path = Path.Combine(LocatePackageDir(), relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"required file {relative} must be shipped");
    }

    [Fact]
    public void TestDirectory_ContainsNodeTestSuite()
    {
        string testDir = Path.Combine(LocatePackageDir(), TestDir);
        Directory.Exists(testDir).Should().BeTrue("the package must ship a Node test suite");
        Directory.GetFiles(testDir, "*.test.js")
            .Should()
            .NotBeEmpty("at least one *.test.js file must be present so `npm test` is non-trivial");
    }

    // ---- package.json shape --------------------------------------------

    [Fact]
    public void PackageJson_IsValidJson()
    {
        string raw = File.ReadAllText(Locate(PackageJsonPath));
        Action act = () => JsonDocument.Parse(raw);
        act.Should().NotThrow("the npm package's package.json must be valid JSON");
    }

    [Fact]
    public void PackageJson_Name_IsScopedCorrectly()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        doc.RootElement.GetProperty("name").GetString().Should().Be("@watermarkremover/mcp");
    }

    [Fact]
    public void PackageJson_Version_IsSemVer()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        string? version = doc.RootElement.GetProperty("version").GetString();
        version.Should().NotBeNullOrEmpty();
        version.Should().MatchRegex(
            "^[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?(\\+[0-9A-Za-z.-]+)?$",
            "the npm package version must be a SemVer string so the GitHub Release tag is unambiguous");
    }

    [Fact]
    public void PackageJson_BinEntry_PointsAtIndexJs()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement bin = doc.RootElement.GetProperty("bin");
        bin.TryGetProperty("watermarkremover-mcp", out JsonElement entry).Should().BeTrue();
        entry.GetString().Should().Be(IndexJsPath);
    }

    [Fact]
    public void PackageJson_Postinstall_RunsPostinstallJs()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement scripts = doc.RootElement.GetProperty("scripts");
        scripts.GetProperty("postinstall").GetString().Should().Be($"node {PostinstallJsPath}");
    }

    [Fact]
    public void PackageJson_TestScript_UsesNodeTest()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement scripts = doc.RootElement.GetProperty("scripts");
        string? testScript = scripts.GetProperty("test").GetString();
        testScript.Should().NotBeNullOrEmpty();
        // Use a glob so the script works on Windows (where node
        // doesn't expand directory globs the way bash does) and on
        // POSIX shells (where the shell expands it before node sees it).
        testScript.Should().StartWith("node --test ", "the test script must invoke the node test runner");
        testScript.Should().Contain("*.test.js", "the test script must target *.test.js files in the test/ directory");
    }

    [Fact]
    public void PackageJson_FilesList_IncludesEveryShippedArtifact()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement.ArrayEnumerator files = doc.RootElement.GetProperty("files").EnumerateArray();
        HashSet<string> shipped = new(StringComparer.Ordinal);
        foreach (JsonElement element in files)
        {
            shipped.Add(element.GetString() ?? string.Empty);
        }

        // npm's default publish set always includes `package.json`,
        // `README`, `LICENSE`, and `CHANGELOG` regardless of the
        // `files` allowlist. We only need to verify the **non-default**
        // files (lib/, postinstall, test suite) are explicitly listed.
        HashSet<string> alwaysIncluded = new(StringComparer.Ordinal)
        {
            "package.json", "README.md", "README", "LICENSE", "CHANGELOG.md",
        };

        foreach (string required in RequiredEntries)
        {
            if (alwaysIncluded.Contains(required))
            {
                continue;
            }
            // Either the file is listed verbatim or it lives under a
            // directory entry (e.g. "lib/"). Match either.
            bool listed = shipped.Contains(required)
                || shipped.Any(entry => required.StartsWith(entry, StringComparison.Ordinal));
            listed.Should().BeTrue($"required file {required} must be covered by the `files` allowlist");
        }
    }

    [Fact]
    public void PackageJson_Engines_RequiresNode18OrLater()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        string? nodeRange = doc.RootElement.GetProperty("engines").GetProperty("node").GetString();
        nodeRange.Should().NotBeNullOrEmpty();
        nodeRange.Should().MatchRegex(@">=1[89]\.|>=2[0-9]\.", "Node 18+ is required (uses node:test, fetch, etc.)");
    }

    [Fact]
    public void PackageJson_Repository_Directory_PointsAtNpmSubfolder()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Locate(PackageJsonPath)));
        JsonElement repo = doc.RootElement.GetProperty("repository");
        repo.GetProperty("type").GetString().Should().Be("git");
        repo.GetProperty("url").GetString().Should().Contain("techbuzzz/ai-watermark-remover");
        repo.GetProperty("directory").GetString().Should().Be(PackageRelativeDir);
    }

    // ---- index.js / postinstall.js content ------------------------------

    [Fact]
    public void IndexJs_IsExecutableShebangAndSpawnsServeMcp()
    {
        string raw = File.ReadAllText(Locate(IndexJsPath));
        raw.Should().StartWith("#!/usr/bin/env node", "index.js must have a Node shebang for npx discovery");
        raw.Should().Contain("serve-mcp", "index.js must invoke the binary with the serve-mcp sub-command");
        raw.Should().Contain("stdio: 'inherit'", "index.js must inherit stdio so the MCP JSON-RPC stream flows through");
    }

    [Fact]
    public void PostinstallJs_HasNodeShebangAndCallsInstallBinary()
    {
        string raw = File.ReadAllText(Locate(PostinstallJsPath));
        raw.Should().StartWith("#!/usr/bin/env node");
        raw.Should().Contain("installBinary", "postinstall must delegate to lib/install.js#installBinary");
        raw.Should().Contain("WR_SKIP_BINARY_DOWNLOAD", "postinstall must honour the skip-download escape hatch");
    }

    [Fact]
    public void BinaryJs_AdvertisesEverySupportedRid()
    {
        string raw = File.ReadAllText(Locate(BinaryJsPath));
        foreach (string rid in SupportedRids)
        {
            raw.Should().Contain(rid, $"binary.js must recognise {rid} (matches the release workflow matrix)");
        }
    }

    [Fact]
    public void InstallJs_HasSoftFailureContract()
    {
        string raw = File.ReadAllText(Locate(InstallJsPath));
        raw.Should().Contain("WR_SKIP_BINARY_DOWNLOAD", "install must skip on the env-var escape hatch");
        raw.Should().Contain("WR_FORCE_BINARY_DOWNLOAD", "install must support forcing a re-download");
        raw.Should().Contain("installBinary", "install must export installBinary for the postinstall hook");
    }

    // ---- helpers --------------------------------------------------------

    private static string LocatePackageDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, PackageRelativeDir);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{PackageRelativeDir}' walking up from {AppContext.BaseDirectory}.");
    }

    private static string Locate(string relative)
    {
        return Path.Combine(LocatePackageDir(), relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
