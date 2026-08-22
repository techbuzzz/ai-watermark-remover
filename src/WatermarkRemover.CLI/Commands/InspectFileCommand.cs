using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Reports (without removing) all metadata found in a file.</summary>
public sealed class InspectFileCommand(IFileCleanerRouter router) : AsyncCommand<InspectFileCommand.Settings>
{
    private readonly IFileCleanerRouter _router = router;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("File to inspect.")]
        public string Path { get; init; } = string.Empty;
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.Path))
        {
            OutputFormatter.Error($"File not found: {settings.Path}");
            return Task.FromResult(1);
        }

        if (!_router.IsSupported(settings.Path))
        {
            OutputFormatter.Error($"Unsupported file type: {System.IO.Path.GetExtension(settings.Path)}");
            return Task.FromResult(1);
        }

        IReadOnlyList<MetadataEntry> entries = _router.Inspect(settings.Path);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(entries);
            return Task.FromResult(0);
        }

        if (entries.Count == 0)
        {
            OutputFormatter.Success("No metadata found.");
            return Task.FromResult(0);
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]{Markup.Escape(System.IO.Path.GetFileName(settings.Path))} metadata[/]")
            .AddColumn("Container")
            .AddColumn("Key")
            .AddColumn("Value");

        foreach (MetadataEntry e in entries)
        {
            table.AddRow(Markup.Escape(e.Container), Markup.Escape(e.Key), Markup.Escape(Truncate(e.Value, 80)));
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "\u2026";
}
