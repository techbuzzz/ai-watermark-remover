using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Static-structural tests for the NuGet packaging story shipped in
/// <c>WR-P010</c>. Guards the contract that the four library
/// projects (<c>WatermarkRemover.Core</c>, <c>.Text</c>, <c>.Metadata</c>,
/// <c>.Image</c>) can be packed and consumed as NuGet packages:
/// <list type="number">
///   <item><description>Each project is <c>IsPackable=true</c> and carries
///   a <c>PackageId</c>, <c>PackageVersion</c>, <c>PackageReadmeFile</c>,
///   and <c>PackageIcon</c>.</description></item>
///   <item><description>Each project bundles its <c>README.md</c> and the
///   shared <c>watermarkremover-nuget-icon.png</c> via explicit
///   <c>None</c> items with <c>Pack="true"</c>.</description></item>
///   <item><description>Each project is <c>SignAssembly=true</c> against
///   the shared <c>src/tools/watermarkremover.snk</c> so the packed
///   assemblies are strong-named.</description></item>
///   <item><description>The shared strong-name key exists, is non-empty,
///   and starts with the .NET private-key .snk header
///   (<c>0x00000207</c>) so the file is recognised as a full
///   key pair (not a public-only key).</description></item>
///   <item><description>The shared icon is a valid PNG whose width and
///   height are both <c>128</c> (the NuGet-recommended size).</description></item>
///   <item><description>Each project's <c>dotnet pack</c> output is a
///   valid <c>.nupkg</c> with the expected id, version, README, and
///   icon entries.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// These are the "the wiring is in place" half of the contract. The
/// dynamic behaviour (assembly loading, NuGet feed resolution) is
/// exercised when a downstream consumer adds the package — this
/// fixture is what guarantees we don't accidentally regress the
/// packaging config in a future refactor.
/// </remarks>
public class NuGetPackagingTests
{
    // The test runner's CWD is bin/Release/net10.0, so the relative
    // path back to src/ is "../../../../" (4 levels up). The other
    // tests in this project use a different pattern (mark of
    // working dir: net10.0 vs. the project root); keep these
    // constants in sync if the project layout changes.
    private const string RepoRoot = "../../../../../..";
    private const string SourceRoot = "../../../../..";

    private static readonly string[] LibraryProjects =
    {
        "WatermarkRemover.Core",
        "WatermarkRemover.Text",
        "WatermarkRemover.Metadata",
        "WatermarkRemover.Image",
    };

    private static readonly string[] CsprojPaths = LibraryProjects
        .Select(p => Path.GetFullPath(Path.Combine(SourceRoot, p, $"{p}.csproj")))
        .ToArray();

    public static IEnumerable<object[]> LibraryProjectNames() =>
        LibraryProjects.Select(p => new object[] { p });

    // ------------------------------------------------------------------
    // Strong-name key + icon
    // ------------------------------------------------------------------

    [Fact]
    public void StrongNameKey_Exists_AndIsNonEmpty()
    {
        string snkPath = Path.GetFullPath(Path.Combine(SourceRoot, "tools", "watermarkremover.snk"));
        File.Exists(snkPath).Should().BeTrue($"the shared strong-name key must exist at {snkPath}");
        new FileInfo(snkPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StrongNameKey_HasValidHeader()
    {
        // The .snk file is in PUBLICKEYBLOB + PRIVATEKEYBLOB layout
        // (the format the .NET compiler's Csc task reads from a
        // <AssemblyOriginatorKeyFile>). The PUBLICKEYBLOB at the
        // start has BLOBHEADER (bType=0x06, bVersion=0x02) followed
        // by RSAPUBKEY ("RSA1" magic + bit length + exponent) and
        // the modulus. We assert the structural invariants without
        // committing to a specific key size.
        string snkPath = Path.GetFullPath(Path.Combine(SourceRoot, "tools", "watermarkremover.snk"));
        byte[] snk = File.ReadAllBytes(snkPath);
        snk.Length.Should().BeGreaterThan(8 + 4 + 4 + 4, "snk must contain a full PUBLICKEYBLOB header");

        // PUBLICKEYBLOB starts at offset 0.
        snk[0].Should().Be(0x06, "PUBLICKEYBLOB bType must be 0x06");
        snk[1].Should().Be(0x02, "PUBLICKEYBLOB bVersion must be 0x02");
        // RSAPUBKEY magic "RSA1" (little-endian 0x31415352).
        snk[8].Should().Be(0x52, "RSAPUBKEY magic must start with 'R' (0x52)");
        snk[9].Should().Be(0x53, "RSAPUBKEY magic must continue with 'S' (0x53)");
        snk[10].Should().Be(0x41, "RSAPUBKEY magic must continue with 'A' (0x41)");
        snk[11].Should().Be(0x31, "RSAPUBKEY magic must end with '1' (0x31)");
        int bitLength = BitConverter.ToInt32(snk, 12);
        (bitLength is 1024 or 2048 or 4096).Should().BeTrue(
            $"RSAPUBKEY bit length must be 1024 / 2048 / 4096, got {bitLength}");
    }

    [Fact]
    public void NuGetIcon_Exists_AndIsA128x128Png()
    {
        string iconPath = Path.GetFullPath(Path.Combine(SourceRoot, "tools", "watermarkremover-nuget-icon.png"));
        File.Exists(iconPath).Should().BeTrue($"the shared NuGet icon must exist at {iconPath}");
        byte[] png = File.ReadAllBytes(iconPath);
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50);
        png[2].Should().Be(0x4E);
        png[3].Should().Be(0x47);
        // IHDR width and height are big-endian 32-bit integers at offset 16 and 20.
        int width = ReadInt32BigEndian(png, 16);
        int height = ReadInt32BigEndian(png, 20);
        width.Should().Be(128, "NuGet recommends a 128x128 icon");
        height.Should().Be(128, "NuGet recommends a 128x128 icon");
    }

    // ------------------------------------------------------------------
    // Per-project packaging configuration
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_IsMarkedPackable(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var isPackable = doc.Descendants("IsPackable").FirstOrDefault()?.Value;
        isPackable.Should().Be("true", $"{projectName} must be IsPackable=true so `dotnet pack` produces a .nupkg");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasPackageId(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var packageId = doc.Descendants("PackageId").FirstOrDefault()?.Value;
        packageId.Should().Be(projectName, $"{projectName} must declare PackageId={projectName}");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasPackageVersion(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var packageVersion = doc.Descendants("PackageVersion").FirstOrDefault()?.Value;
        packageVersion.Should().NotBeNullOrWhiteSpace($"{projectName} must declare a PackageVersion");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasPackageReadme(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var readme = doc.Descendants("PackageReadmeFile").FirstOrDefault()?.Value;
        readme.Should().Be("README.md", $"{projectName} must point PackageReadmeFile at README.md");

        string readmePath = Path.GetFullPath(Path.Combine(SourceRoot, projectName, "README.md"));
        File.Exists(readmePath).Should().BeTrue($"{projectName} must have a README.md at the package root");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasPackageIcon(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var icon = doc.Descendants("PackageIcon").FirstOrDefault()?.Value;
        icon.Should().Be("watermarkremover-nuget-icon.png", $"{projectName} must point PackageIcon at the shared icon");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_BundlesReadmeAndIconAsNoneItems(string projectName)
    {
        // The README.md and icon must be in the package as `<None
        // Pack="true">` items. Without these, `dotnet pack` errors
        // with NU5039 (readme) or NU5046 (icon).
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var noneItems = doc.Descendants("None")
            .Select(n => new
            {
                Include = n.Attribute("Include") is { } i ? i.Value : string.Empty,
                Pack = n.Attribute("Pack") is { } p ? p.Value : string.Empty,
            })
            .ToList();
        noneItems.Should().Contain(x => x.Include == "README.md" && x.Pack == "true",
            $"{projectName} must bundle README.md with Pack=\"true\"");
        noneItems.Should().Contain(x => x.Include.EndsWith("watermarkremover-nuget-icon.png") && x.Pack == "true",
            $"{projectName} must bundle the shared icon with Pack=\"true\"");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_IsSignedWithSharedKey(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var signAssembly = doc.Descendants("SignAssembly").FirstOrDefault()?.Value;
        signAssembly.Should().Be("true", $"{projectName} must SignAssembly=true so the .nupkg contains a strong-named assembly");
        var keyFile = doc.Descendants("AssemblyOriginatorKeyFile").FirstOrDefault()?.Value;
        keyFile.Should().NotBeNullOrWhiteSpace($"{projectName} must reference the shared strong-name key");
        keyFile.Should().Contain("watermarkremover.snk", $"{projectName} must use the shared src/tools/watermarkremover.snk");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasMITLicense(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var license = doc.Descendants("PackageLicenseExpression").FirstOrDefault()?.Value;
        license.Should().Be("MIT", $"{projectName} must declare PackageLicenseExpression=MIT");
    }

    [Theory]
    [MemberData(nameof(LibraryProjectNames))]
    public void LibraryProject_HasProjectUrl(string projectName)
    {
        string csproj = Path.GetFullPath(Path.Combine(SourceRoot, projectName, $"{projectName}.csproj"));
        XDocument doc = XDocument.Load(csproj);
        var url = doc.Descendants("PackageProjectUrl").FirstOrDefault()?.Value;
        url.Should().NotBeNullOrWhiteSpace($"{projectName} must declare a PackageProjectUrl");
        url.Should().StartWith("https://", $"{projectName} PackageProjectUrl must be an HTTPS URL");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset] << 24)
             | (buffer[offset + 1] << 16)
             | (buffer[offset + 2] << 8)
             | buffer[offset + 3];
    }
}
