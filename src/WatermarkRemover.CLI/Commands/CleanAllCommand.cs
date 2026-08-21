using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>
/// Dispatches a single path (file or directory) to the right pipeline per file:
/// markdown → <see cref="IMarkdownCleaner"/>, document/image → <see cref="IFileCleanerRouter"/>,
/// plain text → <see cref="ITextCleaningPipeline"/>. Binary files the router
/// doesn't know about are skipped with a warning, never fed to the text
/// pipeline by accident.
/// </summary>
public sealed class CleanAllCommand(
    IFileCleanerRouter router,
    IMarkdownCleaner markdownCleaner,
    ITextCleaningPipeline textPipeline,
    AppConfig config) : AsyncCommand<CleanAllCommand.Settings>
{
    private readonly IFileCleanerRouter _router = router;
    private readonly IMarkdownCleaner _markdownCleaner = markdownCleaner;
    private readonly ITextCleaningPipeline _textPipeline = textPipeline;
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
        [Description("Suffix appended to cleaned output file names (default: .cleaned).")]
        public string Suffix { get; init; } = ".cleaned";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        CancellationToken ct = CancellationToken.None;
        List<string> files = ResolveFiles(settings);
        if (files.Count == 0)
        {
            OutputFormatter.Error($"No files to clean at: {settings.Path}");
            return 1;
        }

        var succeeded = new List<FileOutcome>();
        var skipped = new List<SkippedFile>();
        var failed = new List<(string Path, string Error)>();

        void Process(string file)
        {
            CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(file, _router);

            if (pipeline == CleanAllClassifier.Pipeline.Unsupported)
            {
                skipped.Add(new SkippedFile(file, $"unsupported extension: {Path.GetExtension(file)}"));
                return;
            }

            try
            {
                FileOutcome outcome = settings.DryRun
                    ? PlanDryRun(file, pipeline)
                    : CleanFile(file, pipeline, settings, ct).GetAwaiter().GetResult();

                succeeded.Add(outcome);
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
                Process(file);
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
                        task.Description = $"[green]{Markup.Escape(Path.GetFileName(file))}[/]";
                        Process(file);
                        task.Increment(1);
                    }
                });
        }

        if (settings.Json)
        {
            OutputFormatter.WriteJson(new
            {
                Succeeded = succeeded,
                Skipped = skipped,
                Failed = failed.Select(f => new { f.Path, f.Error }),
            });
            return failed.Count > 0 ? 1 : 0;
        }

        foreach (FileOutcome r in succeeded)
        {
            OutputFormatter.Success(
                $"{Path.GetFileName(r.InputPath)} \u2192 {Path.GetFileName(r.OutputPath)} ({r.Pipeline})");
        }

        if (skipped.Count > 0)
        {
            AnsiConsole.WriteLine();
            OutputFormatter.Warning($"{skipped.Count} file(s) skipped (unsupported):");
            foreach (SkippedFile s in skipped)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]  - {Markup.Escape(Path.GetFileName(s.Path))}: {Markup.Escape(s.Reason)}[/]");
            }
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

        return failed.Count > 0 ? 1 : 0;
    }

    private async Task<FileOutcome> CleanFile(string file, CleanAllClassifier.Pipeline pipeline, Settings settings, CancellationToken ct)
    {
        string outputPath = ResolveOutputPath(file, settings);

        switch (pipeline)
        {
            case CleanAllClassifier.Pipeline.Markdown:
            {
                string input = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                MarkdownCleanOptions options = BuildMarkdownOptions();
                MarkdownCleanResult result = _markdownCleaner.Clean(input, options);
                await File.WriteAllTextAsync(outputPath, result.Cleaned, ct).ConfigureAwait(false);
                return new FileOutcome(file, outputPath, "markdown");
            }

            case CleanAllClassifier.Pipeline.Text:
            {
                string input = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                TextCleanOptions options = new()
                {
                    EnableUnicode = _config.Text.Layers.Unicode,
                    EnableVendorSpecific = _config.Text.Layers.VendorSpecific,
                    LlmEndpoint = _config.Text.LlmEndpoint,
                    LlmModel = _config.Text.LlmModel,
                };
                TextCleanResult result = await _textPipeline.CleanAsync(input, options, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(outputPath, result.Cleaned, ct).ConfigureAwait(false);
                return new FileOutcome(file, outputPath, "text");
            }

            case CleanAllClassifier.Pipeline.FileMetadata:
            {
                MetadataCleanOptions mdOptions = new()
                {
                    StripExif = _config.Metadata.StripExif,
                    StripXmp = _config.Metadata.StripXmp,
                    StripC2pa = _config.Metadata.StripC2pa,
                    PreserveColorProfile = _config.Metadata.PreserveColorProfile,
                };
                FileCleanResult result = _router.Clean(file, outputPath, mdOptions);
                return new FileOutcome(result.InputPath, result.OutputPath, "metadata");
            }

            default:
                throw new InvalidOperationException($"Unhandled pipeline: {pipeline}");
        }
    }

    private FileOutcome PlanDryRun(string file, CleanAllClassifier.Pipeline pipeline)
    {
        string outputPath = ResolveOutputPath(file, new Settings
        {
            // The ResolveOutputPath helper only reads the suffix / output
            // path; we don't actually write in dry-run so the resulting
            // output path is a description, not a file we touch.
            Suffix = ".cleaned",
        });
        string label = pipeline switch
        {
            CleanAllClassifier.Pipeline.Markdown => "markdown",
            CleanAllClassifier.Pipeline.Text => "text",
            CleanAllClassifier.Pipeline.FileMetadata => "metadata",
            _ => "unsupported",
        };
        return new FileOutcome(file, outputPath, $"dry-run:{label}");
    }

    private MarkdownCleanOptions BuildMarkdownOptions() => new()
    {
        StripHeadings = _config.Markdown.StripHeadings,
        StripCodeFences = _config.Markdown.StripCodeFences,
        StripInlineCode = _config.Markdown.StripInlineCode,
        StripLinks = _config.Markdown.StripLinks,
        StripImages = _config.Markdown.StripImages,
        StripHtml = _config.Markdown.StripHtml,
        StripFrontmatter = _config.Markdown.StripFrontmatter,
        StripAiSignatures = _config.Markdown.StripAiSignatures,
        StripMentions = _config.Markdown.StripMentions,
        StripUnicodeMd = _config.Markdown.StripUnicodeMd,
        StripTrailingWs = _config.Markdown.StripTrailingWs,
    };

    private List<string> ResolveFiles(Settings settings)
    {
        if (File.Exists(settings.Path))
        {
            return [settings.Path];
        }

        if (Directory.Exists(settings.Path))
        {
            SearchOption option = settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return [.. Directory.EnumerateFiles(settings.Path, "*", option)
                .Where(f => !f.Contains(settings.Suffix, StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.Ordinal)];
        }

        return [];
    }

    private static string ResolveOutputPath(string inputPath, Settings settings)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{name}{settings.Suffix}{ext}");
    }

    /// <summary>One successfully classified (and optionally cleaned) file.</summary>
    public sealed record FileOutcome(string InputPath, string OutputPath, string Pipeline);

    /// <summary>One file we intentionally did not clean (binary, unknown).</summary>
    public sealed record SkippedFile(string Path, string Reason);
}
