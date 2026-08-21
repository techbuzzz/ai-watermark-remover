using System.Runtime.Versioning;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>
/// Pure (no I/O) directory resolver for <c>skills/install.sh</c> and
/// <c>skills/install.ps1</c>. Centralising the rules in C# keeps the
/// shell scripts thin and lets us test the resolution logic with
/// xUnit without spawning a subprocess.
/// </summary>
/// <remarks>
/// The shell scripts use the public static methods on this class as
/// their single source of truth — both <c>install.sh</c> and
/// <c>install.ps1</c> compute the target directory the same way
/// (modulo shell-side environment variable syntax).
/// </remarks>
public static class SkillsInstallerTargetResolver
{
    /// <summary>All known agent identifiers the installer understands.</summary>
    public static readonly IReadOnlyList<string> KnownAgents =
    [
        "auto",
        "claude",
        "claude-code",
        "opencode",
        "minimax",
        "minimax-code",
        "cursor",
        "continue",
        "generic",
    ];

    /// <summary>Sub-directory created under the per-agent skills root.</summary>
    public const string SkillSubdir = "watermarkremover";

    /// <summary>
    /// Resolves the target directory for a given agent name. Pure
    /// function — no filesystem access. Pass an <paramref name="env"/>
    /// bag to control which variables the resolver consults, and
    /// <paramref name="cwd"/> to anchor project-relative resolutions
    /// (<c>opencode</c>, <c>generic</c>).
    /// </summary>
    /// <param name="agentName">
    ///   <c>auto</c>, <c>claude</c> / <c>claude-code</c>, <c>opencode</c>,
    ///   <c>minimax</c> / <c>minimax-code</c>, <c>cursor</c>,
    ///   <c>continue</c>, or <c>generic</c>. Case-insensitive.
    /// </param>
    /// <param name="env">
    ///   Environment variables the resolver may consult. Provide
    ///   <c>HOME</c> / <c>USERPROFILE</c> for the user home,
    ///   <c>WATERMARKREMOVER_SKILLS_AGENT</c> to pin the agent under
    ///   <c>auto</c>, and any of <c>WATERMARKREMOVER_SKILLS_CLAUDE_DIR</c>,
    ///   <c>WATERMARKREMOVER_SKILLS_OPENCODE_DIR</c>,
    ///   <c>WATERMARKREMOVER_SKILLS_MINIMAX_DIR</c>,
    ///   <c>WATERMARKREMOVER_SKILLS_GENERIC_DIR</c> to override the
    ///   default path for a specific agent.
    /// </param>
    /// <param name="cwd">Current working directory. Only used for the
    ///   project-local resolutions (<c>opencode</c>, <c>generic</c>)
    ///   and the <c>auto</c> probe.</param>
    /// <param name="projectMarkers">
    ///   Optional set of relative paths under <paramref name="cwd"/>
    ///   that exist. Used by the <c>auto</c> probe to detect which
    ///   agent the project is configured for. Pass
    ///   <c>Directory.Exists</c> results for <c>.opencode</c>,
    ///   <c>.claude</c>, <c>.minimax</c>, etc. Defaults to
    ///   <c>empty</c> (no markers — the probe falls through to
    ///   <c>generic</c>).
    /// </param>
    /// <returns>The resolved target directory plus the canonical
    ///   agent id. The directory may or may not exist on disk; the
    ///   installer creates it if missing.</returns>
    /// <exception cref="ArgumentException">Unknown agent name.</exception>
    public static ResolvedTarget Resolve(
        string? agentName,
        IReadOnlyDictionary<string, string?> env,
        string cwd,
        IReadOnlySet<string>? projectMarkers = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);

        var canonical = Canonicalize(agentName);

        // `auto` — pinned env var wins, then marker probe, then generic.
        if (canonical == "auto")
        {
            var pinned = GetEnv(env, "WATERMARKREMOVER_SKILLS_AGENT");
            if (!string.IsNullOrWhiteSpace(pinned))
            {
                canonical = Canonicalize(pinned);
            }
            else
            {
                canonical = ProbeAgent(cwd, projectMarkers);
            }
        }

        return canonical switch
        {
            "claude"        => ResolveUnderHome(env, "WATERMARKREMOVER_SKILLS_CLAUDE_DIR",  ".claude",    "claude"),
            "opencode"      => ResolveProjectLocal(env, "WATERMARKREMOVER_SKILLS_OPENCODE_DIR", cwd, ".opencode", "opencode"),
            "minimax"       => ResolveUnderHome(env, "WATERMARKREMOVER_SKILLS_MINIMAX_DIR", ".minimax",   "minimax"),
            "cursor"        => ResolveUnderHome(env, null,                                  ".cursor",    "cursor"),
            "continue"      => ResolveUnderHome(env, null,                                  ".continue",  "continue"),
            "generic"       => ResolveProjectLocal(env, "WATERMARKREMOVER_SKILLS_GENERIC_DIR", cwd, null,    "generic"),
            _ => throw new ArgumentException(
                $"Unknown agent '{agentName}'. Known: {string.Join(", ", KnownAgents)}.",
                nameof(agentName)),
        };
    }

    /// <summary>The result of <see cref="Resolve"/>.</summary>
    /// <param name="Agent">Canonical agent id (lower-case, kebab-case).</param>
    /// <param name="Directory">Absolute target directory.</param>
    public readonly record struct ResolvedTarget(string Agent, string Directory)
    {
        /// <summary>Convenience: full path including the skill subdir.</summary>
        public string SkillDirectory =>
            System.IO.Path.Combine(Directory, SkillsInstallerTargetResolver.SkillSubdir);
    }

    // ---- helpers --------------------------------------------------------

    private static string Canonicalize(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return "auto";
        }

        var trimmed = agentName.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "auto"           => "auto",
            "claude-code"    => "claude",
            "claude"         => "claude",
            "opencode"       => "opencode",
            "minimax-code"   => "minimax",
            "minimax"        => "minimax",
            "minimaxcode"    => "minimax",
            "cursor"         => "cursor",
            "continue"       => "continue",
            "generic"        => "generic",
            _                => trimmed, // unknown — let the switch throw
        };
    }

    private static string ProbeAgent(
        string cwd,
        IReadOnlySet<string>? projectMarkers)
    {
        if (projectMarkers is not null)
        {
            if (projectMarkers.Contains(Path.Combine(cwd, ".opencode"))) { return "opencode"; }
            if (projectMarkers.Contains(Path.Combine(cwd, ".claude")))   { return "claude"; }
            if (projectMarkers.Contains(Path.Combine(cwd, ".minimax")))  { return "minimax"; }
        }
        return "generic";
    }

    private static ResolvedTarget ResolveUnderHome(
        IReadOnlyDictionary<string, string?> env,
        string? overrideKey,
        string relativeDir,
        string agent)
    {
        var overridePath = GetEnv(env, overrideKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return new ResolvedTarget(agent, overridePath);
        }

        var home = HomeDirectory(env)
            ?? throw new InvalidOperationException(
                $"Cannot resolve a home-relative {agent} target: " +
                "neither HOME nor USERPROFILE is set in the environment.");

        return new ResolvedTarget(agent, Path.Combine(home, relativeDir, "skills"));
    }

    private static ResolvedTarget ResolveProjectLocal(
        IReadOnlyDictionary<string, string?> env,
        string? overrideKey,
        string cwd,
        string? projectMarker,
        string agent)
    {
        var overridePath = GetEnv(env, overrideKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return new ResolvedTarget(agent, overridePath);
        }

        // If a project marker (e.g. .opencode/) exists, anchor under
        // the cwd. Otherwise fall back to ~/.config/<dir>/ for shared
        // installs — same place the user expects.
        if (projectMarker is not null)
        {
            return new ResolvedTarget(agent, Path.Combine(cwd, projectMarker, "skills"));
        }

        var home = HomeDirectory(env);
        return home is null
            ? new ResolvedTarget(agent, Path.Combine(cwd, "skills"))
            : new ResolvedTarget(agent, Path.Combine(home, ".config", "watermarkremover", "skills"));
    }

    /// <summary>Returns the best-effort home directory, or null.</summary>
    public static string? HomeDirectory(IReadOnlyDictionary<string, string?> env)
    {
        var home = GetEnv(env, "HOME");
        if (!string.IsNullOrWhiteSpace(home)) { return home; }
        var userprofile = GetEnv(env, "USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userprofile)) { return userprofile; }
        return null;
    }

    private static string? GetEnv(
        IReadOnlyDictionary<string, string?> env,
        string? key)
    {
        if (key is null) { return null; }
        return env.TryGetValue(key, out var value) ? value : null;
    }
}
