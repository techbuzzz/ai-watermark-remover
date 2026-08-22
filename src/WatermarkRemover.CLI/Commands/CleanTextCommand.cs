using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Cleans a plain-text payload through Layers A/B/C.</summary>
public sealed class CleanTextCommand(ITextCleaningPipeline pipeline, AppConfig config)
    : AsyncCommand<CleanTextCommand.Settings>
{
    private readonly ITextCleaningPipeline _pipeline = pipeline;
    private readonly AppConfig _config = config;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[TEXT]")]
        [Description("Text to clean. Omit to read from --input or stdin.")]
        public string? Text { get; init; }

        [CommandOption("-i|--input <PATH>")]
        [Description("Read input text from this file.")]
        public string? Input { get; init; }

        [CommandOption("--statistical")]
        [Description("Enable Layer B statistical / green-list rewriting.")]
        public bool Statistical { get; init; }

        [CommandOption("--no-unicode")]
        [Description("Disable Layer A unicode hygiene.")]
        public bool NoUnicode { get; init; }

        [CommandOption("--no-vendor")]
        [Description("Disable Layer C vendor-specific detection.")]
        public bool NoVendor { get; init; }

        [CommandOption("--llm-endpoint <URL>")]
        [Description("LLM endpoint for Layer B back-translation.")]
        public string? LlmEndpoint { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string input = await IoHelper.ReadTextAsync(settings.Text, settings.Input, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.Error("No input provided. Pass text as an argument, use --input, or pipe via stdin.");
            return 1;
        }

        TextCleanOptions options = new()
        {
            EnableUnicode = !settings.NoUnicode && _config.Text.Layers.Unicode,
            EnableStatistical = settings.Statistical || _config.Text.Layers.Statistical,
            EnableVendorSpecific = !settings.NoVendor && _config.Text.Layers.VendorSpecific,
            LlmEndpoint = settings.LlmEndpoint ?? _config.Text.LlmEndpoint,
            LlmModel = _config.Text.LlmModel,
        };

        TextCleanResult result = await _pipeline.CleanAsync(input, options, cancellationToken).ConfigureAwait(false);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(result);
            return 0;
        }

        if (!settings.DryRun)
        {
            await IoHelper.WriteTextAsync(settings.Output, result.Cleaned, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Verbose || settings.DryRun)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Removed {result.RemovedItems.Count} item(s), {result.Detections.Count} detection(s). Confidence {result.Confidence:P0}.[/]");
            foreach (RemovedItem item in result.RemovedItems)
            {
                AnsiConsole.MarkupLineInterpolated($"[grey]  - {item.Type} @ {item.Position}: {item.Description}[/]");
            }
        }

        return 0;
    }
}
