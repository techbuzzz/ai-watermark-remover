using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Detects (without removing) watermark signatures in plain text.</summary>
public sealed class DetectTextCommand(ITextCleaningPipeline pipeline) : AsyncCommand<DetectTextCommand.Settings>
{
    private readonly ITextCleaningPipeline _pipeline = pipeline;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[TEXT]")]
        [Description("Text to inspect. Omit to read from --input or stdin.")]
        public string? Text { get; init; }

        [CommandOption("-i|--input <PATH>")]
        [Description("Read input text from this file.")]
        public string? Input { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        CancellationToken ct = CancellationToken.None;
        string input = await IoHelper.ReadTextAsync(settings.Text, settings.Input, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.Error("No input provided.");
            return 1;
        }

        IReadOnlyList<WatermarkMatch> matches = _pipeline.Detect(input);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(matches);
            return 0;
        }

        if (matches.Count == 0)
        {
            OutputFormatter.Success("No watermark signatures detected.");
            return 0;
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Detected watermarks[/]")
            .AddColumn("Vendor")
            .AddColumn("Pattern")
            .AddColumn("Position")
            .AddColumn("Length")
            .AddColumn("Confidence");

        foreach (WatermarkMatch m in matches)
        {
            table.AddRow(
                Markup.Escape(m.Vendor),
                Markup.Escape(m.Pattern),
                m.Position.ToString(),
                m.Length.ToString(),
                $"{m.Confidence:P0}");
        }

        AnsiConsole.Write(table);
        return matches.Count > 0 ? 2 : 0;
    }
}
