using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Detects (without inpainting) visual watermark regions in an image.</summary>
public sealed class DetectWatermarkCommand(IImageCleaningPipeline pipeline, AppConfig config)
    : AsyncCommand<DetectWatermarkCommand.Settings>
{
    private readonly IImageCleaningPipeline _pipeline = pipeline;
    private readonly AppConfig _config = config;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<IMAGE>")]
        [Description("Path to the image to inspect.")]
        public string Image { get; init; } = string.Empty;

        [CommandOption("--threshold <VALUE>")]
        [Description("Auto-detection confidence threshold (0-1).")]
        public double? Threshold { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.Image))
        {
            OutputFormatter.Error($"Image not found: {settings.Image}");
            return Task.FromResult(1);
        }

        ImageCleanOptions options = new()
        {
            ModelPath = _config.Image.ModelPath,
            AutoDetectThreshold = settings.Threshold ?? _config.Image.AutoDetectThreshold,
            BlendEdges = _config.Image.BlendEdges,
        };

        IReadOnlyList<DetectedRegion> regions = _pipeline.Detect(settings.Image, options);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(regions);
            return Task.FromResult(0);
        }

        if (regions.Count == 0)
        {
            OutputFormatter.Success("No visual watermark regions detected.");
            return Task.FromResult(0);
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Detected regions[/]")
            .AddColumn("X")
            .AddColumn("Y")
            .AddColumn("Width")
            .AddColumn("Height")
            .AddColumn("Confidence");

        foreach (DetectedRegion r in regions)
        {
            table.AddRow(
                r.X.ToString(),
                r.Y.ToString(),
                r.Width.ToString(),
                r.Height.ToString(),
                $"{r.Confidence:P0}");
        }

        AnsiConsole.Write(table);
        return Task.FromResult(2);
    }
}
