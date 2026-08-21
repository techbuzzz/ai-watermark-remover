using System.Reflection;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>
/// Surfaces the assembly version of the running CLI binary. Backed by
/// <c>&lt;InformationalVersion&gt;</c> in
/// <c>src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj</c> via the
/// <see cref="AssemblyInformationalVersionAttribute"/> that the SDK emits
/// for us; the <see cref="FallbackVersion"/> is the value the SDK would
/// generate if no <c>&lt;Version&gt;</c> were set at all (a
/// deterministic, never-empty placeholder so the <c>--version</c> flag
/// always prints something).
/// </summary>
/// <remarks>
/// The helper is split out (rather than reading the attribute inline in
/// <c>Program.cs</c>) so tests can verify the surface — both the
/// "the value is never null or whitespace" invariant and the "the value
/// reported by the running binary matches the csproj" invariant.
/// </remarks>
public static class VersionInfo
{
    /// <summary>
    /// Used when no <c>&lt;InformationalVersion&gt;</c> is set on the
    /// entry assembly. Should never happen in practice; kept so a
    /// <c>--version</c> call is still meaningful on a misconfigured
    /// local build.
    /// </summary>
    public const string FallbackVersion = "0.0.0+local";

    /// <summary>
    /// The version string printed by <c>watermarkremover --version</c>.
    /// Resolution order:
    /// <list type="number">
    ///   <item><see cref="AssemblyInformationalVersionAttribute"/> on the entry assembly (set by the SDK from <c>&lt;InformationalVersion&gt;</c>).</item>
    ///   <item><see cref="AssemblyVersion"/> on the entry assembly (set by the SDK from <c>&lt;AssemblyVersion&gt;</c>).</item>
    ///   <item><see cref="FallbackVersion"/>.</item>
    /// </list>
    /// </summary>
    public static string Current { get; } = Resolve(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    private static string Resolve(Assembly assembly)
    {
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return assemblyVersion.ToString();
        }

        return FallbackVersion;
    }
}
