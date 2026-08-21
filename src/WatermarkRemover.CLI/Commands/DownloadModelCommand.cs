using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Downloads and extracts the LaMa ONNX inpainting model.</summary>
public sealed class DownloadModelCommand(IModelDownloader downloader) : AsyncCommand<DownloadModelCommand.Settings>
{
    private readonly IModelDownloader _downloader = downloader;

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-d|--dest <DIR>")]
        [Description("Destination directory for the model (default: ./models).")]
        public string Destination { get; init; } = "./models";

        [CommandOption("--force")]
        [Description("Re-download even if the model already exists.")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        CancellationToken ct = CancellationToken.None;

        try
        {
            string modelPath = await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
                .StartAsync(async ctx =>
                {
                    ProgressTask task = ctx.AddTask("[green]Downloading model[/]", maxValue: 1.0);
                    Progress<double> progress = new(p => task.Value = Math.Clamp(p, 0, 1));
                    return await _downloader.DownloadAsync(settings.Destination, settings.Force, progress, ct).ConfigureAwait(false);
                }).ConfigureAwait(false);

            if (settings.Json)
            {
                OutputFormatter.WriteJson(new { ModelPath = modelPath });
            }
            else
            {
                OutputFormatter.Success($"Model ready at {modelPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
            {
                OutputFormatter.WriteJson(new { Error = ex.Message });
            }
            else
            {
                OutputFormatter.Error($"Model download failed: {ex.Message}");
            }

            return 1;
        }
    }
}
