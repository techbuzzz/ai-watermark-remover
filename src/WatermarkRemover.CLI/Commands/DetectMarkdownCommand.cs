using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Detects (without removing) AI artifacts in a markdown document.</summary>
public sealed class DetectMarkdownCommand(IMarkdownCleaner cleaner) : AsyncCommand<DetectMarkdownCommand.Settings>
{
    private readonly IMarkdownCleaner _cleaner = cleaner;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[MARKDOWN]")]
        [Description("Markdown to inspect. Omit to read from --input or stdin.")]
        public string? Markdown { get; init; }

        [CommandOption("-i|--input <PATH>")]
        [Description("Read input markdown from this file.")]
        public string? Input { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string input = await IoHelper.ReadTextAsync(settings.Markdown, settings.Input, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.Error("No input provided.");
            return 1;
        }

        IReadOnlyList<AiArtifact> artifacts = _cleaner.Detect(input);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(artifacts);
            return 0;
        }

        if (artifacts.Count == 0)
        {
            OutputFormatter.Success("No AI artifacts detected.");
            return 0;
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Detected AI artifacts[/]")
            .AddColumn("Type")
            .AddColumn("Description")
            .AddColumn("Line")
            .AddColumn("Column");

        foreach (AiArtifact a in artifacts)
        {
            table.AddRow(
                Markup.Escape(a.Type),
                Markup.Escape(a.Description),
                a.Line.ToString(),
                a.Column.ToString());
        }

        AnsiConsole.Write(table);
        return 2;
    }
}
