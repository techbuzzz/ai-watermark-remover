using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Strips metadata from one or more files (single file, directory or glob-like batch).</summary>
public sealed class CleanFileCommand(IFileCleanerRouter router, AppConfig config)
    : AsyncCommand<CleanFileCommand.Settings>
{
    private readonly IFileCleanerRouter _router = router;
    private readonly AppConfig _config = config;

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("File or directory to clean.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-r|--recursive")]
        [Description("Recurse into sub-directories when PATH is a directory.")]
        public bool Recursive { get; init; }

        [CommandOption("--suffix <SUFFIX>")]
        [Description("Suffix appended to the cleaned output file name (default: .cleaned).")]
        public string Suffix { get; init; } = ".cleaned";
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        List<string> files = ResolveFiles(settings);
        if (files.Count == 0)
        {
            OutputFormatter.Error($"No supported files found at: {settings.Path}");
            return Task.FromResult(1);
        }

        MetadataCleanOptions options = new()
        {
            StripExif = _config.Metadata.StripExif,
            StripXmp = _config.Metadata.StripXmp,
            StripC2pa = _config.Metadata.StripC2pa,
            PreserveColorProfile = _config.Metadata.PreserveColorProfile,
        };

        List<FileCleanResult> succeeded = [];
        List<(string Path, string Error)> failed = [];

        void ProcessOne(string file)
        {
            try
            {
                string outputPath = ResolveOutputPath(file, settings);
                if (settings.DryRun)
                {
                    IReadOnlyList<MetadataEntry> found = _router.Inspect(file);
                    succeeded.Add(new FileCleanResult(file, outputPath, found, new FileInfo(file).Length, 0, TimeSpan.Zero));
                    return;
                }

                FileCleanResult result = _router.Clean(file, outputPath, options);
                succeeded.Add(result);
            }
            catch (Exception ex)
            {
                failed.Add((file, ex.Message));
            }
        }

        if (settings.Json || files.Count == 1)
        {
            foreach (string file in files)
            {
                ProcessOne(file);
            }
        }
        else
        {
            AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                .Start(ctx =>
                {
                    ProgressTask task = ctx.AddTask("[green]Cleaning files[/]", maxValue: files.Count);
                    foreach (string file in files)
                    {
                        task.Description = $"[green]{Markup.Escape(System.IO.Path.GetFileName(file))}[/]";
                        ProcessOne(file);
                        task.Increment(1);
                    }
                });
        }

        if (settings.Json)
        {
            OutputFormatter.WriteJson(new
            {
                Succeeded = succeeded,
                Failed = failed.Select(f => new { f.Path, f.Error }),
            });
            return Task.FromResult(failed.Count > 0 ? 1 : 0);
        }

        foreach (FileCleanResult r in succeeded)
        {
            OutputFormatter.Success($"{System.IO.Path.GetFileName(r.InputPath)} \u2192 {System.IO.Path.GetFileName(r.OutputPath)} ({r.RemovedEntries.Count} entries removed)");
        }

        if (failed.Count > 0)
        {
            AnsiConsole.WriteLine();
            OutputFormatter.Warning($"{failed.Count} file(s) failed:");
            foreach ((string path, string error) in failed)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]  - {System.IO.Path.GetFileName(path)}: {error}[/]");
            }
        }

        return Task.FromResult(failed.Count > 0 ? 1 : 0);
    }

    private List<string> ResolveFiles(Settings settings)
    {
        if (File.Exists(settings.Path))
        {
            return _router.IsSupported(settings.Path) ? [settings.Path] : [];
        }

        if (Directory.Exists(settings.Path))
        {
            SearchOption option = settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return [.. Directory.EnumerateFiles(settings.Path, "*", option)
                .Where(f => _router.IsSupported(f) && !f.Contains(settings.Suffix, StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.Ordinal)];
        }

        return [];
    }

    private static string ResolveOutputPath(string inputPath, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Output))
        {
            if (Directory.Exists(settings.Output))
            {
                return System.IO.Path.Combine(settings.Output, System.IO.Path.GetFileName(inputPath));
            }

            return settings.Output;
        }

        string dir = System.IO.Path.GetDirectoryName(inputPath) ?? ".";
        string name = System.IO.Path.GetFileNameWithoutExtension(inputPath);
        string ext = System.IO.Path.GetExtension(inputPath);
        return System.IO.Path.Combine(dir, $"{name}{settings.Suffix}{ext}");
    }
}
