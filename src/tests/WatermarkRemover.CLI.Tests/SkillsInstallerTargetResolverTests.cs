using FluentAssertions;
using WatermarkRemover.CLI.Infrastructure;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Tests for <see cref="SkillsInstallerTargetResolver"/> — the unit
/// under test that backs <c>skills/install.sh</c> and
/// <c>skills/install.ps1</c>. The acceptance gate from WR-S14 is
/// "the installer script finds the correct target directory"; this
/// fixture covers that resolution directly, without spawning a
/// subprocess, so it runs on every developer machine and in CI.
/// </summary>
public class SkillsInstallerTargetResolverTests
{
    private const string Cwd = "/home/tester/projects/myrepo";
    private const string Home = "/home/tester";

    private static Dictionary<string, string?> EnvWith(
        string? home = Home,
        string? pinned = null,
        string? claudeOverride = null,
        string? opencodeOverride = null,
        string? minimaxOverride = null,
        string? genericOverride = null)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (home is not null)            { env["HOME"] = home; }
        if (pinned is not null)          { env["WATERMARKREMOVER_SKILLS_AGENT"] = pinned; }
        if (claudeOverride is not null)  { env["WATERMARKREMOVER_SKILLS_CLAUDE_DIR"] = claudeOverride; }
        if (opencodeOverride is not null){ env["WATERMARKREMOVER_SKILLS_OPENCODE_DIR"] = opencodeOverride; }
        if (minimaxOverride is not null) { env["WATERMARKREMOVER_SKILLS_MINIMAX_DIR"] = minimaxOverride; }
        if (genericOverride is not null) { env["WATERMARKREMOVER_SKILLS_GENERIC_DIR"] = genericOverride; }
        return env;
    }

    // ---- agent-name matrix --------------------------------------------

    [Theory]
    [InlineData("claude",      "claude")]
    [InlineData("claude-code", "claude")]
    [InlineData("CLAUDE",      "claude")]
    [InlineData("Claude-Code", "claude")]
    public void Resolve_Claude_Family_RoutesToClaudeAgent(string input, string expectedAgent)
    {
        var target = SkillsInstallerTargetResolver.Resolve(input, EnvWith(), Cwd);
        target.Agent.Should().Be(expectedAgent);
        target.Directory.Should().Be(Path.Combine(Home, ".claude", "skills"));
        target.SkillDirectory.Should().Be(Path.Combine(Home, ".claude", "skills", "watermarkremover"));
    }

    [Theory]
    [InlineData("opencode", "opencode")]
    [InlineData("OPENCODE", "opencode")]
    public void Resolve_Opencode_RoutesToProjectLocalDotOpencode(string input, string expectedAgent)
    {
        var target = SkillsInstallerTargetResolver.Resolve(input, EnvWith(), Cwd);
        target.Agent.Should().Be(expectedAgent);
        target.Directory.Should().Be(Path.Combine(Cwd, ".opencode", "skills"));
    }

    [Theory]
    [InlineData("minimax",       "minimax")]
    [InlineData("minimax-code",  "minimax")]
    [InlineData("minimaxcode",   "minimax")]
    [InlineData("MINIMAX",       "minimax")]
    public void Resolve_MiniMax_Family_RoutesToHomeDotMinimax(string input, string expectedAgent)
    {
        var target = SkillsInstallerTargetResolver.Resolve(input, EnvWith(), Cwd);
        target.Agent.Should().Be(expectedAgent);
        target.Directory.Should().Be(Path.Combine(Home, ".minimax", "skills"));
    }

    [Fact]
    public void Resolve_Cursor_RoutesToHomeDotCursor()
    {
        var target = SkillsInstallerTargetResolver.Resolve("cursor", EnvWith(), Cwd);
        target.Agent.Should().Be("cursor");
        target.Directory.Should().Be(Path.Combine(Home, ".cursor", "skills"));
    }

    [Fact]
    public void Resolve_Continue_RoutesToHomeDotContinue()
    {
        var target = SkillsInstallerTargetResolver.Resolve("continue", EnvWith(), Cwd);
        target.Agent.Should().Be("continue");
        target.Directory.Should().Be(Path.Combine(Home, ".continue", "skills"));
    }

    [Fact]
    public void Resolve_Generic_FallsBackToHomeConfigWatermarkremover()
    {
        var target = SkillsInstallerTargetResolver.Resolve("generic", EnvWith(), Cwd);
        target.Agent.Should().Be("generic");
        target.Directory.Should().Be(Path.Combine(Home, ".config", "watermarkremover", "skills"));
    }

    [Fact]
    public void Resolve_NullOrWhitespace_DefaultsToAuto()
    {
        // Null and whitespace both fall through to `auto` and, with no
        // project markers, fall back to `generic`.
        var fromNull = SkillsInstallerTargetResolver.Resolve(null, EnvWith(), Cwd);
        fromNull.Agent.Should().Be("generic");

        var fromEmpty = SkillsInstallerTargetResolver.Resolve("   ", EnvWith(), Cwd);
        fromEmpty.Agent.Should().Be("generic");
    }

    [Fact]
    public void Resolve_UnknownAgent_ThrowsArgumentException()
    {
        var act = () => SkillsInstallerTargetResolver.Resolve("nope", EnvWith(), Cwd);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown agent 'nope'*");
    }

    // ---- auto-detect probes -------------------------------------------

    [Fact]
    public void Resolve_Auto_PinnedByEnv_OverridesMarker()
    {
        // Pinned to `claude` even though an .opencode marker is present.
        var markers = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(Cwd, ".opencode"),
        };
        var target = SkillsInstallerTargetResolver.Resolve(
            "auto",
            EnvWith(pinned: "claude"),
            Cwd,
            markers);
        target.Agent.Should().Be("claude");
    }

    [Fact]
    public void Resolve_Auto_MarkerOpencodeWinsOverMarkerClaude()
    {
        var markers = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(Cwd, ".claude"),
            Path.Combine(Cwd, ".opencode"),
        };
        var target = SkillsInstallerTargetResolver.Resolve("auto", EnvWith(), Cwd, markers);
        target.Agent.Should().Be("opencode");
    }

    [Fact]
    public void Resolve_Auto_NoMarkers_FallsBackToGeneric()
    {
        var target = SkillsInstallerTargetResolver.Resolve("auto", EnvWith(), Cwd, projectMarkers: null);
        target.Agent.Should().Be("generic");
    }

    [Fact]
    public void Resolve_Auto_MarkerOnlyUnderCwd_IsRecognized()
    {
        // Marker paths are passed as absolute; the resolver still
        // matches when the project lives at `Cwd`.
        var markers = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(Cwd, ".opencode"),
        };
        var target = SkillsInstallerTargetResolver.Resolve("auto", EnvWith(), Cwd, markers);
        target.Agent.Should().Be("opencode");
        target.Directory.Should().Be(Path.Combine(Cwd, ".opencode", "skills"));
    }

    // ---- environment overrides -----------------------------------------

    [Fact]
    public void Resolve_ClaudeOverride_RewritesTarget()
    {
        var custom = "/opt/share/wr-skills";
        var target = SkillsInstallerTargetResolver.Resolve(
            "claude", EnvWith(claudeOverride: custom), Cwd);
        target.Directory.Should().Be(custom);
    }

    [Fact]
    public void Resolve_OpencodeOverride_RewritesTarget()
    {
        var custom = "/srv/wr/opencode";
        var target = SkillsInstallerTargetResolver.Resolve(
            "opencode", EnvWith(opencodeOverride: custom), Cwd);
        target.Directory.Should().Be(custom);
    }

    [Fact]
    public void Resolve_MiniMaxOverride_RewritesTarget()
    {
        var custom = "/srv/wr/minimax";
        var target = SkillsInstallerTargetResolver.Resolve(
            "minimax", EnvWith(minimaxOverride: custom), Cwd);
        target.Directory.Should().Be(custom);
    }

    [Fact]
    public void Resolve_GenericOverride_RewritesTarget()
    {
        var custom = "/srv/wr/generic";
        var target = SkillsInstallerTargetResolver.Resolve(
            "generic", EnvWith(genericOverride: custom), Cwd);
        target.Directory.Should().Be(custom);
    }

    [Fact]
    public void Resolve_BlankOverride_FallsBackToDefault()
    {
        // Empty / whitespace-only env values must be ignored, not
        // treated as a real override.
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = Home,
            ["WATERMARKREMOVER_SKILLS_CLAUDE_DIR"] = "   ",
        };
        var target = SkillsInstallerTargetResolver.Resolve("claude", env, Cwd);
        target.Directory.Should().Be(Path.Combine(Home, ".claude", "skills"));
    }

    // ---- home resolution fallback -------------------------------------

    [Fact]
    public void Resolve_NoHome_FallsBackToUserProfile()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USERPROFILE"] = @"C:\Users\tester",
        };
        var target = SkillsInstallerTargetResolver.Resolve("claude", env, Cwd);
        target.Directory.Should().Be(Path.Combine(@"C:\Users\tester", ".claude", "skills"));
    }

    [Fact]
    public void Resolve_NoHome_GenericFallsBackToCwd()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var target = SkillsInstallerTargetResolver.Resolve("generic", env, Cwd);
        target.Directory.Should().Be(Path.Combine(Cwd, "skills"));
    }

    [Fact]
    public void Resolve_NoHome_ClaudeTarget_Throws()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var act = () => SkillsInstallerTargetResolver.Resolve("claude", env, Cwd);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*home-relative claude target*");
    }

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Resolve_NullEnv_Throws()
    {
        var act = () => SkillsInstallerTargetResolver.Resolve("claude", null!, Cwd);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyCwd_Throws(string cwd)
    {
        var act = () => SkillsInstallerTargetResolver.Resolve("claude", EnvWith(), cwd);
        act.Should().Throw<ArgumentException>();
    }

    // ---- known agents list --------------------------------------------

    [Fact]
    public void KnownAgents_ContainsEveryDocumentedName()
    {
        // Acceptance: the resolver and the install scripts share the
        // same canonical list. Lock the contract here.
        SkillsInstallerTargetResolver.KnownAgents.Should().BeEquivalentTo(new[]
        {
            "auto",
            "claude",
            "claude-code",
            "opencode",
            "minimax",
            "minimax-code",
            "cursor",
            "continue",
            "generic",
        });
    }

    [Fact]
    public void SkillSubdir_IsWatermarkremover()
    {
        // Acceptance: every install path lands under a `watermarkremover/`
        // sub-folder so multiple skills can coexist in the agent's
        // skills directory.
        SkillsInstallerTargetResolver.SkillSubdir.Should().Be("watermarkremover");
    }
}
