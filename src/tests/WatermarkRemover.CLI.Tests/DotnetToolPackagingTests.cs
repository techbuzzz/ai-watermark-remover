using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Static-structural tests for the <c>dotnet tool</c> global-tool
/// packaging story shipped in <c>WR-P011</c>. The CLI project
/// (<c>src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj</c>) must
/// be packable into a NuGet <c>.nupkg</c> that the
/// <c>dotnet</c> host can install as a global tool:
/// <list type="number">
///   <item><description>The csproj sets
///   <c>IsPackable=true</c> + <c>PackAsTool=true</c> +
///   <c>ToolCommandName=watermarkremover</c> +
///   <c>PackageId=watermarkremover</c> +
///   <c>PackageOutputType=Exe</c>.</description></item>
///   <item><description>The csproj bundles the per-tool
///   <c>README.md</c> and the shared
///   <c>src/tools/watermarkremover-nuget-icon.png</c> as
///   <c>&lt;None Pack="true"&gt;</c> items so the packed
///   <c>.nupkg</c> includes both (without these <c>dotnet pack</c>
///   errors with <c>NU5039</c> / <c>NU5046</c>).</description></item>
///   <item><description>A fresh <c>dotnet pack</c> of the CLI project
///   produces a valid <c>.nupkg</c> whose <c>.nuspec</c> declares a
///   <c>&lt;packageType name="DotnetTool"/&gt;</c> and whose
///   <c>tools/&lt;tfm&gt;/any/DotnetToolSettings.xml</c> wires the
///   <c>watermarkremover</c> command to the entry-point assembly.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// These are the "the wiring is in place" half of the contract. The
/// dynamic behaviour (a real <c>dotnet tool install -g</c> round-trip
/// landing the command on <c>$PATH</c>) is exercised manually and is
/// out of scope for CI because it would mutate the runner's global
/// tool store. The shared <c>PackFixture</c> runs <c>dotnet pack</c>
/// once per test class (a static cache + lock) and writes the
/// resulting <c>.nupkg</c> to <c>out/tool-pack/</c> under the repo
/// root, where it is easy to inspect by hand and to <c>.gitignore</c>.
/// </remarks>
public sealed class DotnetToolPackagingTests
{
    private const string CliCsprojRelativePath = "src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj";
    private const string CliReadmeRelativePath = "src/WatermarkRemover.CLI/README.md";
    private const string SharedIconRelativePath = "src/tools/watermarkremover-nuget-icon.png";

    // ------------------------------------------------------------------
    // Shared pack fixture: one pack per test class (the dynamic
    // .nupkg assertions all read the same artifact). The fixture
    // memoises its work in a static `Lazy<T>` so xUnit's parallel
    // test scheduler does not trigger duplicate `dotnet pack` runs.
    // ------------------------------------------------------------------

    public sealed class PackFixture
    {
        public string PackArtifactPath { get; }

        public PackFixture()
        {
            PackArtifactPath = SharedArtifact.Value;
        }

        private static readonly Lazy<string> SharedArtifact = new(() =>
        {
            string outputDir = Path.GetFullPath(
                Path.Combine(LocateRepoRoot(), "out", "tool-pack"));
            Directory.CreateDirectory(outputDir);
            return PackCliProject(outputDir);
        });
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string LocateRepoRoot()
    {
        // AppContext.BaseDirectory points at the test bin output, e.g.
        //   src/tests/WatermarkRemover.CLI.Tests/bin/Debug/net10.0/
        // Walk up until we find Directory.Build.props + global.json;
        // both must be present, otherwise we are not in the repo.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Directory.Build.props")) &&
                File.Exists(Path.Combine(dir, "global.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }

    private static string Locate(string relative) =>
        Path.Combine(LocateRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static XDocument LoadCliCsproj() => XDocument.Load(Locate(CliCsprojRelativePath));

    private static string PackCliProject(string outputDir)
    {
        string csproj = Locate(CliCsprojRelativePath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Debug");
        psi.ArgumentList.Add("--no-restore");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputDir);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `dotnet pack`");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`dotnet pack` failed with exit code {process.ExitCode}.\n" +
                $"STDOUT:\n{stdout}\n" +
                $"STDERR:\n{stderr}");
        }

        // The .nupkg filename is `<id>.<version>.nupkg`; pick the
        // newest one so this test stays green when the version is
        // bumped in the csproj.
        string[] nupkgs = Directory
            .GetFiles(outputDir, "watermarkremover.*.nupkg")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        if (nupkgs.Length == 0)
        {
            throw new FileNotFoundException(
                $"`dotnet pack` succeeded but no `watermarkremover.*.nupkg` was produced in {outputDir}.");
        }
        return nupkgs[0];
    }

    private static string ReadEntryAsString(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"the .nupkg must contain {entryName}");
        using Stream stream = entry!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------
    // csproj: tool packaging wiring
    // ------------------------------------------------------------------

    [Fact]
    public void CliProject_IsMarkedPackable()
    {
        XDocument doc = LoadCliCsproj();
        var isPackable = doc.Descendants("IsPackable").FirstOrDefault()?.Value;
        isPackable.Should().Be("true",
            "the CLI csproj must be IsPackable=true so `dotnet pack` produces a .nupkg");
    }

    [Fact]
    public void CliProject_IsMarkedPackAsTool()
    {
        XDocument doc = LoadCliCsproj();
        var packAsTool = doc.Descendants("PackAsTool").FirstOrDefault()?.Value;
        packAsTool.Should().Be("true",
            "the CLI csproj must be PackAsTool=true so the packed .nupkg is a .NET global tool");
    }

    [Fact]
    public void CliProject_HasToolCommandName()
    {
        XDocument doc = LoadCliCsproj();
        var toolCommandName = doc.Descendants("ToolCommandName").FirstOrDefault()?.Value;
        toolCommandName.Should().Be("watermarkremover",
            "the tool command name must match the binary so `dotnet tool install -g watermarkremover` " +
            "exposes a `watermarkremover` command on $PATH");
    }

    [Fact]
    public void CliProject_HasPackageId()
    {
        XDocument doc = LoadCliCsproj();
        var packageId = doc.Descendants("PackageId").FirstOrDefault()?.Value;
        packageId.Should().Be("watermarkremover",
            "the CLI's PackageId must be `watermarkremover` so the install command reads " +
            "`dotnet tool install -g watermarkremover`");
    }

    [Fact]
    public void CliProject_HasPackageOutputTypeExe()
    {
        XDocument doc = LoadCliCsproj();
        var outputType = doc.Descendants("PackageOutputType").FirstOrDefault()?.Value;
        outputType.Should().Be("Exe",
            "PackageOutputType=Exe is mandatory for tool packages — it tells the SDK to " +
            "wrap the assembly as a `dotnet`-rider apphost inside the .nupkg");
    }

    [Fact]
    public void CliProject_StaysAtNet10_0()
    {
        // The tool contract depends on the tool assembly targeting
        // net10.0 — `PackageOutputType=Exe` plus a TFM pin keeps the
        // restore graph in the .nupkg scoped to a single TFM and the
        // `tools/<tfm>/any/` folder stable.
        XDocument doc = LoadCliCsproj();
        var tfm = doc.Descendants("TargetFramework").FirstOrDefault()?.Value;
        tfm.Should().Be("net10.0",
            "the CLI project must target net10.0 so the tool runs on the latest LTS runtime");
    }

    [Fact]
    public void CliProject_HasFrameworkReference_AspNetCore()
    {
        // The `serve` and `serve-mcp` commands both host ASP.NET
        // Core. The csproj must keep the `Microsoft.AspNetCore.App`
        // framework reference so the global tool brings it in via the
        // `.nuspec`'s `<frameworkReferences>` block (and the
        // apphost at install time).
        string raw = File.ReadAllText(Locate(CliCsprojRelativePath));
        raw.Should().Contain("Microsoft.AspNetCore.App",
            "the CLI must reference Microsoft.AspNetCore.App so the `serve` / `serve-mcp` " +
            "commands have an HTTP host at install time");
    }

    // ------------------------------------------------------------------
    // csproj: README + icon bundling
    // ------------------------------------------------------------------

    [Fact]
    public void CliProject_HasPackageReadmeFile()
    {
        XDocument doc = LoadCliCsproj();
        var readme = doc.Descendants("PackageReadmeFile").FirstOrDefault()?.Value;
        readme.Should().Be("README.md",
            "PackageReadmeFile must point at the per-tool README so nuget.org / `dotnet tool` " +
            "install both render the install / uninstall / command list");
        File.Exists(Locate(CliReadmeRelativePath))
            .Should().BeTrue("the per-tool README.md must exist next to the CLI csproj");
    }

    [Fact]
    public void CliProject_HasPackageIcon()
    {
        XDocument doc = LoadCliCsproj();
        var icon = doc.Descendants("PackageIcon").FirstOrDefault()?.Value;
        icon.Should().Be("watermarkremover-nuget-icon.png",
            "PackageIcon must point at the shared 128x128 icon so the gallery listing is consistent " +
            "with the four library packages");
    }

    [Fact]
    public void CliProject_BundlesReadmeAndIconAsNoneItems()
    {
        // The README and icon must be packed via explicit
        // `<None Pack="true">` items. Without these `dotnet pack`
        // errors with NU5039 (readme) or NU5046 (icon).
        XDocument doc = LoadCliCsproj();
        var noneItems = doc.Descendants("None")
            .Select(n => new
            {
                Include = n.Attribute("Include") is { } i ? i.Value : string.Empty,
                Pack = n.Attribute("Pack") is { } p ? p.Value : string.Empty,
            })
            .ToList();
        noneItems.Should().Contain(x => x.Include == "README.md" && x.Pack == "true",
            "the CLI csproj must bundle README.md with Pack=\"true\"");
        noneItems.Should().Contain(x => x.Include.EndsWith("watermarkremover-nuget-icon.png") && x.Pack == "true",
            "the CLI csproj must bundle the shared icon with Pack=\"true\"");
    }

    [Fact]
    public void SharedIcon_FileExists_AtTheRelativePath()
    {
        // The icon path is relative to the csproj; verify the file
        // the csproj references actually exists.
        File.Exists(Locate(SharedIconRelativePath))
            .Should().BeTrue($"the shared icon must exist at {SharedIconRelativePath} for the pack to succeed");
    }

    [Fact]
    public void CliReadme_HasInstallAndCommandsSections()
    {
        // The README is the landing page for `dotnet tool install -g
        // watermarkremover` (it is the `readme` shown by
        // `dotnet tool list`). It must include the install command,
        // the most-used CLI commands, and a link to the main repo.
        string readme = File.ReadAllText(Locate(CliReadmeRelativePath));
        readme.Should().Contain("dotnet tool install",
            "the per-tool README must show the `dotnet tool install` command");
        readme.Should().Contain("watermarkremover",
            "the per-tool README must mention the `watermarkremover` command by name");
        readme.Should().Contain("ai-watermark-remover",
            "the per-tool README must link back to the main GitHub repo for the full reference");
    }

    // ------------------------------------------------------------------
    // .nupkg: shape (read after a fresh `dotnet pack`)
    // ------------------------------------------------------------------

    [Fact]
    public void Pack_ArtifactExists_AfterDotnetPack()
    {
        PackFixture fixture = new();
        File.Exists(fixture.PackArtifactPath)
            .Should().BeTrue($"`dotnet pack` must produce a .nupkg at {fixture.PackArtifactPath}");
    }

    [Fact]
    public void Pack_Nuspec_DeclaresDotnetToolPackageType()
    {
        // The .nuspec's `<packageTypes>` block is what `dotnet tool`
        // uses to recognise a package as a tool. The SDK writes
        // `<packageType name="DotnetTool" />` automatically when
        // `PackAsTool=true`.
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        string nuspec = ReadEntryAsString(archive, "watermarkremover.nuspec");
        nuspec.Should().Contain("DotnetTool",
            "the .nuspec must declare <packageType name=\"DotnetTool\"/> so the .nupkg is " +
            "installable via `dotnet tool install -g`");
    }

    [Fact]
    public void Pack_Nuspec_HasWatermarkremoverAsId()
    {
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        string nuspec = ReadEntryAsString(archive, "watermarkremover.nuspec");
        nuspec.Should().Contain("<id>watermarkremover</id>",
            "the .nuspec id must be `watermarkremover` so `dotnet tool install -g watermarkremover` resolves");
    }

    [Fact]
    public void Pack_Nuspec_AdvertisesAspNetCoreFrameworkReference()
    {
        // The `<frameworkReferences>` block in the .nuspec is what
        // causes the apphost at install time to require
        // Microsoft.AspNetCore.App. The CLI csproj has the
        // `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
        // item, and the SDK should propagate it.
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        string nuspec = ReadEntryAsString(archive, "watermarkremover.nuspec");
        nuspec.Should().Contain("Microsoft.AspNetCore.App",
            "the .nuspec must declare a framework reference to Microsoft.AspNetCore.App " +
            "so the tool can host Kestrel after `dotnet tool install -g`");
    }

    [Fact]
    public void Pack_Nuspec_ReferencesBundledReadmeAndIcon()
    {
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        string nuspec = ReadEntryAsString(archive, "watermarkremover.nuspec");
        nuspec.Should().Contain("<readme>README.md</readme>",
            "the .nuspec must reference the bundled README.md");
        nuspec.Should().Contain("<icon>watermarkremover-nuget-icon.png</icon>",
            "the .nuspec must reference the bundled icon");
    }

    [Fact]
    public void Pack_ToolSettings_RegistersWatermarkremoverCommand()
    {
        // `tools/<tfm>/any/DotnetToolSettings.xml` is the manifest
        // `dotnet` reads to wire the `ToolCommandName` to the
        // entry-point assembly. It must be present and must point at
        // the `watermarkremover.dll` entry point.
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        ZipArchiveEntry? settings = archive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith("DotnetToolSettings.xml",
                StringComparison.OrdinalIgnoreCase));
        settings.Should().NotBeNull(
            "the .nupkg must contain a tools/<tfm>/any/DotnetToolSettings.xml that maps " +
            "the tool command to the entry-point assembly");

        string xml = ReadEntryAsString(archive, settings!.FullName);
        XDocument parsed = XDocument.Parse(xml);
        var command = parsed.Descendants("Command").FirstOrDefault();
        command.Should().NotBeNull("DotnetToolSettings.xml must declare a <Command> element");
        command!.Attribute("Name")?.Value.Should().Be("watermarkremover",
            "the <Command Name> must be `watermarkremover` so `dotnet tool install -g watermarkremover` " +
            "registers the right binary on $PATH");
        command.Attribute("EntryPoint")?.Value.Should().Be("watermarkremover.dll",
            "the <Command EntryPoint> must be the CLI's main assembly");
        command.Attribute("Runner")?.Value.Should().Be("dotnet",
            "the <Command Runner> must be `dotnet` so the apphost is resolved from the installed SDK");
    }

    [Fact]
    public void Pack_Contains_EntryPointAssemblyUnderTools()
    {
        // The main assembly must live under `tools/<tfm>/any/` so the
        // `dotnet` host can find it at install time. Spot-check that
        // one of the `tools/net10.0/any/watermarkremover.dll` entry
        // exists.
        PackFixture fixture = new();
        using ZipArchive archive = ZipFile.OpenRead(fixture.PackArtifactPath);
        archive.Entries
            .Select(e => e.FullName.Replace('/', Path.DirectorySeparatorChar))
            .Should().Contain(p => p.StartsWith($"tools{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}any{Path.DirectorySeparatorChar}watermarkremover.dll"),
                "the .nupkg must contain the entry-point assembly under tools/net10.0/any/");
    }

    [Fact]
    public void Pack_ManifestSize_IsReasonable_ForATool()
    {
        // Sanity check: a tool package that bundles the ONNX runtime
        // for every supported RID is naturally large (~110 MB
        // uncompressed), and the SkiaSharp native binaries for
        // Windows / Linux / macOS add another ~25 MB. The .nupkg
        // itself should still be under 250 MB after `Deflate`
        // compression. The threshold is loose on purpose; tightening
        // it would flake on minor ONNX / SkiaSharp updates.
        PackFixture fixture = new();
        long size = new FileInfo(fixture.PackArtifactPath).Length;
        size.Should().BeGreaterThan(1_000_000,
            "a tool that bundles the ONNX runtime for every supported RID must be > 1 MB");
        size.Should().BeLessThan(250_000_000,
            "the .nupkg must stay under 250 MB — a future refactor that accidentally bundles the " +
            "Astro UI source maps or a debug `wwwroot` would blow this up");
    }
}
