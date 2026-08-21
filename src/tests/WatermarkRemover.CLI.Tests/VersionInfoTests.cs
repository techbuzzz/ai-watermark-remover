using FluentAssertions;
using WatermarkRemover.CLI.Infrastructure;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Covers the surface that backs <c>watermarkremover --version</c>:
/// the assembly version the binary reports (<see cref="VersionInfo"/>)
/// and the early-exit handler in <see cref="CliShortCircuits"/>.
/// </summary>
/// <remarks>
/// These tests do not spawn the compiled binary — they exercise the
/// pure, testable seams that <c>Program.cs</c> delegates to. The
/// end-to-end behaviour (<c>dotnet run -- --version</c> prints
/// <c>watermarkremover 1.0.0</c>) is verified manually on each release
/// and exercised by the <see cref="CliShortCircuitsTests"/> below.
/// </remarks>
public class VersionInfoTests
{
    [Fact]
    public void Current_IsNeverEmpty()
    {
        // Invariant: --version must always print *something*. If the
        // SDK is misconfigured and emits no InformationalVersion
        // attribute, the fallback string takes over rather than a
        // blank line.
        VersionInfo.Current.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Current_DoesNotContainSurroundingWhitespace()
    {
        // We feed the value straight into `watermarkremover {version}`;
        // any leading/trailing whitespace from a malformed attribute
        // would leak through to stdout.
        VersionInfo.Current.Should().Be(VersionInfo.Current.Trim());
    }

    [Fact]
    public void FallbackVersion_IsStableNonEmptyString()
    {
        // The fallback is referenced from the binary loader as a
        // last-ditch value; locking it down prevents silent drift.
        VersionInfo.FallbackVersion.Should().NotBeNullOrWhiteSpace();
    }
}

/// <summary>
/// Covers <see cref="CliShortCircuits.TryHandle"/> — the early-exit
/// path that lets <c>--version</c> skip config + DI + logging entirely.
/// </summary>
public class CliShortCircuitsTests
{
    [Fact]
    public void TryHandle_LongVersionFlag_ReturnsZero_AndWritesVersion()
    {
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["--version"], capture);

        exit.Should().Be(0, because: "a successful short-circuit always exits 0");
        capture.ToString().Should().Be($"watermarkremover {VersionInfo.Current}{Environment.NewLine}");
    }

    [Fact]
    public void TryHandle_ShortVersionFlag_ReturnsZero_AndWritesVersion()
    {
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["-V"], capture);

        exit.Should().Be(0);
        capture.ToString().Should().StartWith("watermarkremover ");
        capture.ToString().Should().Contain(VersionInfo.Current);
    }

    [Fact]
    public void TryHandle_VersionFlagAmongOtherArgs_StillShortCircuits()
    {
        // The flag is a *global* short-circuit, not a per-command one:
        // `watermarkremover clean-text foo --version` should still print
        // the version rather than running the command with a stray arg.
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["clean-text", "foo", "--version"], capture);

        exit.Should().Be(0);
        capture.ToString().Should().Contain(VersionInfo.Current);
    }

    [Theory]
    [InlineData("clean-text", "hello")]
    [InlineData("clean-markdown")]
    [InlineData("serve")]
    [InlineData("completions", "--shell", "bash")]
    public void TryHandle_NoVersionFlag_ReturnsNull(params string[] argv)
    {
        // Any non-version invocation must fall through to the regular
        // CommandApp pipeline.
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(argv, capture);

        exit.Should().BeNull(because: "CliShortCircuits must only short-circuit on its own flags");
        capture.ToString().Should().BeEmpty(because: "nothing was written to the sink");
    }

    [Fact]
    public void TryHandle_EmptyArgs_ReturnsNull()
    {
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle([], capture);

        exit.Should().BeNull();
        capture.ToString().Should().BeEmpty();
    }

    [Fact]
    public void TryHandle_VerboseFlag_ReturnsNull()
    {
        // `-v` is `--verbose`, not `--version`. A user running
        // `watermarkremover --verbose serve` must get verbose logging,
        // not a version dump.
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["--verbose", "serve"], capture);

        exit.Should().BeNull();
        capture.ToString().Should().BeEmpty();
    }

    [Fact]
    public void TryHandle_LowerVAlone_ShortCircuitsToVersion()
    {
        // The help table that Spectre emits (`-v, --version`) implies
        // a bare `watermarkremover -v` is a version request. Honour it
        // — anything else would be a UX wart, since the user just read
        // that line and followed it.
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["-v"], capture);

        exit.Should().Be(0);
        capture.ToString().Should().Contain(VersionInfo.Current);
    }

    [Fact]
    public void TryHandle_LowerVWithOtherArgs_FallsThrough()
    {
        // `-v` paired with a command is left to Spectre (which still
        // treats it as `--version` and prints the raw version string,
        // not the `watermarkremover …` formatted output). This test
        // only asserts that *our* short-circuit doesn't claim it; the
        // Spectre behaviour that follows is unchanged.
        using StringWriter capture = new();
        int? exit = CliShortCircuits.TryHandle(["-v", "serve"], capture);

        exit.Should().BeNull();
        capture.ToString().Should().BeEmpty();
    }

    [Fact]
    public void TryHandle_DefaultsToConsoleOut_WhenWriterIsOmitted()
    {
        // The default sink is Console.Out — exercising it via the
        // overload that accepts a writer keeps the test hermetic while
        // still confirming the call shape Program.cs uses.
        using StringWriter capture = new();
        Console.SetOut(capture);
        try
        {
            int? exit = CliShortCircuits.TryHandle(["--version"]);
            exit.Should().Be(0);
            capture.ToString().Should().Contain(VersionInfo.Current);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void TryHandle_NullArgs_Throws()
    {
        Action act = () => CliShortCircuits.TryHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
