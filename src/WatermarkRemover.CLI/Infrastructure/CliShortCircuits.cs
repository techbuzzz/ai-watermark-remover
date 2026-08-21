namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>
/// Pre-CommandApp short-circuit handlers — flags that must be honoured
/// before <c>Configuration</c> is loaded, Serilog is wired up, or the
/// DI container is built. Today this is just <c>--version</c>;
/// adding a new short-circuit is a one-line addition to
/// <see cref="TryHandle"/>.
/// </summary>
/// <remarks>
/// <para>
/// The short-circuit runs <em>before</em> anything else because:
/// <list type="bullet">
///   <item>It must not depend on <c>config.yaml</c> being present.</item>
///   <item>It must not touch logging — a CI script that pipes the output
///         into a version-compare should not have log lines muddying stdout.</item>
///   <item>It must not require the LaMa model or any other on-disk asset.</item>
/// </list>
/// </para>
/// <para>
/// The full flow is split into this helper (pure, testable, side-effect
/// only on the supplied <see cref="TextWriter"/>) and the top-level
/// wiring in <c>Program.cs</c>, which calls
/// <see cref="TryHandle"/> and exits with the returned code on a hit.
/// </para>
/// <para>
/// <strong>Why the short-circuit is needed.</strong> Spectre.Console.Cli
/// 0.49.0 auto-registers <c>-v, --version</c> from the entry assembly's
/// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
/// That would work (it would report <c>1.0.0+&lt;githash&gt;</c>), but
/// the raw version string isn't prefixed with the binary name and
/// Spectre's default output goes to <see cref="Console.Out"/> only
/// after <c>CommandApp</c> parses every token, which means a stray
/// <c>--verbose</c> elsewhere on the line would still trip its
/// prefix-match against <c>--version</c> (a pre-existing bug, not in
/// scope for this helper). Running our own short-circuit gives us
/// control of the format and a deterministic exit code.
/// </para>
/// </remarks>
public static class CliShortCircuits
{
    /// <summary>Long-form version flag. Unambiguous.</summary>
    public const string LongVersionToken = "--version";

    /// <summary>
    /// Short-form version flag, uppercase <c>V</c> to avoid colliding
    /// with <c>--verbose</c> / <c>-v</c>.
    /// </summary>
    public const string UpperVToken = "-V";

    /// <summary>
    /// Lowercase <c>v</c> is bound to <c>--verbose</c>. We treat a bare
    /// <c>-v</c> (no other args) as <c>--version</c> so the help table
    /// that Spectre prints (<c>-v, --version    Prints version
    /// information</c>) is consistent with the actual user-facing
    /// behaviour. When <c>-v</c> is paired with other args, it stays
    /// attached to the verbose-logging flow.
    /// </summary>
    public const string LowerVToken = "-v";

    /// <summary>
    /// Tokens that always trigger the version short-circuit, regardless
    /// of what else is on the command line.
    /// </summary>
    public static readonly IReadOnlyCollection<string> AlwaysVersionTokens = new[] { LongVersionToken, UpperVToken };

    /// <summary>
    /// Inspects <paramref name="args"/> for a short-circuit flag. If one
    /// is present, writes the corresponding response to
    /// <paramref name="writer"/> and returns the exit code (always
    /// <c>0</c> for a successful short-circuit). If no short-circuit
    /// applies, returns <c>null</c> so the caller can fall through to
    /// the normal <c>CommandApp</c> path.
    /// </summary>
    /// <param name="args">The full argv. Treated as opaque tokens —
    ///   positional arguments are ignored because <c>--version</c> is a
    ///   global flag, not a command.</param>
    /// <param name="writer">Where to write the response. Defaults to
    ///   <see cref="Console.Out"/> so tests can inject a
    ///   <see cref="StringWriter"/> and capture the bytes.</param>
    public static int? TryHandle(IReadOnlyList<string> args, TextWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        TextWriter sink = writer ?? Console.Out;

        // Always-version tokens win regardless of what else is on the
        // command line: `watermarkremover clean-text foo --version` is
        // a version request, not a clean-text request with a stray arg.
        if (args.Any(token => AlwaysVersionTokens.Contains(token, StringComparer.Ordinal)))
        {
            WriteVersion(sink);
            return 0;
        }

        // Bare `-v` (no other args) is treated as `--version` so the
        // help table that Spectre prints — `-v, --version    Prints
        // version information` — matches what the user actually sees
        // when they type `watermarkremover -v`. When `-v` is paired
        // with another token, it falls through to the existing
        // `--verbose` flow in `Program.cs`.
        bool hasLowerV = args.Any(token => string.Equals(token, LowerVToken, StringComparison.Ordinal));
        bool hasAnythingElse = args.Any(token => !string.Equals(token, LowerVToken, StringComparison.Ordinal));
        if (hasLowerV && !hasAnythingElse)
        {
            WriteVersion(sink);
            return 0;
        }

        return null;
    }

    private static void WriteVersion(TextWriter sink) =>
        sink.WriteLine($"watermarkremover {VersionInfo.Current}");
}
