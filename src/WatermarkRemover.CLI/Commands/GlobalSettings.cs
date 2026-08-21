using System.ComponentModel;
using Spectre.Console.Cli;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Options common to every command.</summary>
public class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit machine-readable JSON instead of rich console output.")]
    public bool Json { get; init; }

    [CommandOption("-v|--verbose")]
    [Description("Enable verbose diagnostic logging.")]
    public bool Verbose { get; init; }

    [CommandOption("--dry-run")]
    [Description("Analyse and report changes without writing any output files.")]
    public bool DryRun { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write result to this path instead of stdout / in-place.")]
    public string? Output { get; init; }

    [CommandOption("-c|--config <PATH>")]
    [Description("Path to a config.yaml file.")]
    public string? Config { get; init; }
}
