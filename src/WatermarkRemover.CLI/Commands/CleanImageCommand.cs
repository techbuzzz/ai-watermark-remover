using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Removes visual watermarks from an image via mask generation + LaMa inpainting.</summary>
public sealed class CleanImageCommand(IImageCleaningPipeline pipeline, AppConfig config)
    : AsyncCommand<CleanImageCommand.Settings>
{
    private readonly IImageCleaningPipeline _pipeline = pipeline;
    private readonly AppConfig _config = config;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<IMAGE>")]
        [Description("Path to the input image.")]
        public string Image { get; init; } = string.Empty;

        [CommandOption("--mask <PATH>")]
        [Description("Explicit mask PNG (white = inpaint). Omit for auto-detection.")]
        public string? Mask { get; init; }

        [CommandOption("--model <PATH>")]
        [Description("Path to the big-lama ONNX model.")]
        public string? Model { get; init; }

        [CommandOption("--threshold <VALUE>")]
        [Description("Auto-detection confidence threshold (0-1).")]
        public double? Threshold { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {

        if (!File.Exists(settings.Image))
        {
            OutputFormatter.Error($"Image not found: {settings.Image}");
            return 1;
        }

        string outputPath = settings.Output ?? BuildDefaultOutput(settings.Image);

        ImageCleanOptions options = new()
        {
            ModelPath = settings.Model ?? _config.Image.ModelPath,
            MaskPath = settings.Mask,
            AutoDetectThreshold = settings.Threshold ?? _config.Image.AutoDetectThreshold,
            BlendEdges = _config.Image.BlendEdges,
        };

        if (settings.DryRun)
        {
            IReadOnlyList<DetectedRegion> regions = _pipeline.Detect(settings.Image, options);
            if (settings.Json)
            {
                OutputFormatter.WriteJson(regions);
            }
            else
            {
                OutputFormatter.Warning($"Dry-run: {regions.Count} region(s) would be inpainted.");
            }

            return 0;
        }

        ImageCleanResult result = await _pipeline.CleanAsync(settings.Image, outputPath, options, cancellationToken).ConfigureAwait(false);

        if (settings.Json)
        {
            OutputFormatter.WriteJson(result);
            return 0;
        }

        OutputFormatter.Success($"Cleaned image written to {result.OutputPath} (model: {result.ModelUsed}, {result.DetectedWatermarks.Count} region(s)).");
        return 0;
    }

    private static string BuildDefaultOutput(string inputPath)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{name}.cleaned{ext}");
    }
}
