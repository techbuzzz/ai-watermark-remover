using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Image;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;

// Resolve --config early (Spectre parses it per-command, but config drives DI registration).
string? configPath = ExtractOption(args, "--config", "-c");
AppConfig config = ConfigLoader.Load(configPath);

bool verbose = args.Contains("--verbose") || args.Contains("-v");
LogEventLevel level = verbose
    ? LogEventLevel.Debug
    : Enum.TryParse(config.Logging.Level, ignoreCase: true, out LogEventLevel parsed) ? parsed : LogEventLevel.Information;

LoggerConfiguration logConfig = new LoggerConfiguration().MinimumLevel.Is(level);
if (config.Logging.Output.Contains("console", StringComparison.OrdinalIgnoreCase))
{
    logConfig = logConfig.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
}

if (config.Logging.Output.Contains("file", StringComparison.OrdinalIgnoreCase))
{
    logConfig = logConfig.WriteTo.File("logs/watermarkremover-.log", rollingInterval: RollingInterval.Day);
}

Log.Logger = logConfig.CreateLogger();

try
{
    ServiceCollection services = new();
    services.AddWatermarkRemoverCore(config);
    services.AddWatermarkRemoverText();
    services.AddWatermarkRemoverMetadata();
    services.AddWatermarkRemoverImage();
    services.AddSingleton(Log.Logger);
    services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));

    TypeRegistrar registrar = new(services);
    CommandApp app = new(registrar);
    app.Configure(cfg =>
    {
        cfg.SetApplicationName("watermarkremover");
        cfg.AddCommand<CleanTextCommand>("clean-text").WithDescription("Clean plain text (Layers A/B/C).");
        cfg.AddCommand<CleanMarkdownCommand>("clean-markdown").WithDescription("Clean markdown, preserving code blocks.");
        cfg.AddCommand<CleanFileCommand>("clean-file").WithDescription("Strip metadata from files (batch capable).");
        cfg.AddCommand<CleanImageCommand>("clean-image").WithDescription("Remove visual watermarks via inpainting.");
        cfg.AddCommand<CleanAllCommand>("clean-all").WithDescription("Auto-route a path: dispatches each file to text, markdown, or metadata pipeline by extension.");
        cfg.AddCommand<DetectTextCommand>("detect-text").WithDescription("Detect watermark signatures in text.");
        cfg.AddCommand<DetectMarkdownCommand>("detect-markdown").WithDescription("Detect AI artifacts in markdown.");
        cfg.AddCommand<DetectWatermarkCommand>("detect-watermark").WithDescription("Detect visual watermark regions in an image.");
        cfg.AddCommand<InspectFileCommand>("inspect-file").WithDescription("Report metadata found in a file.");
        cfg.AddCommand<DownloadModelCommand>("download-model").WithDescription("Download the LaMa ONNX inpainting model.");
        cfg.AddCommand<ServeCommand>("serve").WithDescription("Host the HTTP API.");

#if DEBUG
        cfg.PropagateExceptions();
        cfg.ValidateExamples();
#endif
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
    OutputFormatter.Error(ex.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static string? ExtractOption(string[] args, params string[] names)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (names.Contains(args[i], StringComparer.Ordinal))
        {
            return args[i + 1];
        }
    }

    foreach (string arg in args)
    {
        foreach (string name in names)
        {
            string prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }
    }

    return null;
}
