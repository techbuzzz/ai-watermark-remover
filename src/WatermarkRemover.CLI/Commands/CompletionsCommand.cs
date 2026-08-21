using System.ComponentModel;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;

namespace WatermarkRemover.CLI.Commands;

/// <summary>
/// Emits a shell completion script to stdout. Pipe the output to the
/// shell's completion directory (bash / zsh) or to the user profile
/// (PowerShell / fish) — see <c>docs/SHELL-COMPLETION.md</c>.
/// </summary>
/// <remarks>
/// The generator is static and the command list is curated, so this
/// command does not need DI for any of the core services. It does
/// NOT honour <c>--json</c>: the entire point of the command is to
/// emit a shell script on stdout, so any wrapper output (panels, JSON)
/// would be a bug.
/// </remarks>
public sealed class CompletionsCommand : AsyncCommand<CompletionsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shell <SHELL>")]
        [Description("Target shell. One of: bash, zsh, powershell, fish.")]
        public string Shell { get; init; } = "bash";
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Shell))
        {
            OutputFormatter.Error($"--shell is required. Supported: {string.Join(", ", ShellCompletionScripts.SupportedShells)}");
            return Task.FromResult(1);
        }

        try
        {
            string script = ShellCompletionScripts.Render(settings.Shell);
            Console.Out.Write(script);
            return Task.FromResult(0);
        }
        catch (ArgumentException ex)
        {
            OutputFormatter.Error(ex.Message);
            return Task.FromResult(1);
        }
    }
}
