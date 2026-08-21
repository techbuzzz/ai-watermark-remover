using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Cleans a markdown document while preserving code blocks.</summary>
public sealed class CleanMarkdownCommand(IMarkdownCleaner cleaner) : AsyncCommand<CleanMarkdownCommand.Settings>
{
    private readonly IMarkdownCleaner _cleaner = cleaner;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[MARKDOWN]")]
        [Description("Markdown to clean. Omit to read from --input or stdin.")]
        public string? Markdown { get; init; }

        [CommandOption("-i|--input <PATH>")]
        [Description("Read input markdown from this file.")]
        public string? Input { get; init; }

        [CommandOption("--strip-all")]
        [Description("Enable every markdown transform.")]
        public bool StripAll { get; init; }

        [CommandOption("--strip-code-fences")]
        [Description("Also strip fenced code blocks.")]
        public bool StripCodeFences { get; init; }

        [CommandOption("--strip-links")]
        [Description("Also strip hyperlinks.")]
        public bool StripLinks { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        CancellationToken ct = CancellationToken.None;
        string input = await IoHelper.ReadTextAsync(settings.Markdown, settings.Input, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.Error("No input provided. Pass markdown as an argument, use --input, or pipe via stdin.");
            return 1;
        }

        MarkdownCleanOptions options = settings.StripAll
            ? MarkdownCleanOptions.StripAll()
            : new MarkdownCleanOptions
            {
                StripCodeFences = settings.StripCodeFences,
                StripLinks = settings.StripLinks,
            };

        MarkdownCleanResult result = _cleaner.Clean(input, options);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(result);
            return 0;
        }

        if (!settings.DryRun)
        {
            await IoHelper.WriteTextAsync(settings.Output, result.Cleaned, ct).ConfigureAwait(false);
        }

        if (settings.Verbose || settings.DryRun)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Removed {result.RemovedItems.Count} item(s); {result.CodeBlocksPreserved}/{result.CodeBlocksFound} code block(s) preserved; frontmatter removed: {result.FrontmatterRemoved}.[/]");
        }

        return 0;
    }
}
