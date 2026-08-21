using System.ComponentModel;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Commands;

/// <summary>Hosts the watermark-remover HTTP API (ASP.NET Core Minimal API).</summary>
public sealed class ServeCommand(
    ITextCleaningPipeline textPipeline,
    IMarkdownCleaner markdownCleaner,
    IFileCleanerRouter fileRouter,
    IImageCleaningPipeline imagePipeline,
    AppConfig config) : AsyncCommand<ServeCommand.Settings>
{
    private readonly ITextCleaningPipeline _textPipeline = textPipeline;
    private readonly IMarkdownCleaner _markdownCleaner = markdownCleaner;
    private readonly IFileCleanerRouter _fileRouter = fileRouter;
    private readonly IImageCleaningPipeline _imagePipeline = imagePipeline;
    private readonly AppConfig _config = config;

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--host <HOST>")]
        [Description("Host/interface to bind (default: 0.0.0.0).")]
        public string Host { get; init; } = "0.0.0.0";

        [CommandOption("-p|--port <PORT>")]
        [Description("Port to listen on (default: 5080).")]
        public int Port { get; init; } = 5080;

        [CommandOption("--api-key <KEY>")]
        [Description("Require this API key via the X-API-Key header. Omit to disable auth.")]
        public string? ApiKey { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{settings.Host}:{settings.Port}");

        // Reuse the already-constructed pipeline services.
        builder.Services.AddSingleton(_textPipeline);
        builder.Services.AddSingleton(_markdownCleaner);
        builder.Services.AddSingleton(_fileRouter);
        builder.Services.AddSingleton(_imagePipeline);
        builder.Services.AddSingleton(_config);

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                string key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        WebApplication app = builder.Build();
        app.UseRateLimiter();

        // API-key auth middleware (only when a key is configured).
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            string requiredKey = settings.ApiKey;
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health"))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided) || provided != requiredKey)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new ErrorResult(ErrorCodes.InvalidInput, "Missing or invalid API key.")).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        MapEndpoints(app);

        OutputFormatter.Success($"WatermarkRemover API listening on http://{settings.Host}:{settings.Port}");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            OutputFormatter.Warning("API key authentication is ENABLED (X-API-Key header required).");
        }

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // POST /clean/text
        app.MapPost("/clean/text", async (TextRequest req, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(req.Text))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'text' is required."));
            }

            TextCleanOptions options = new()
            {
                EnableUnicode = req.EnableUnicode ?? _config.Text.Layers.Unicode,
                EnableStatistical = req.EnableStatistical ?? _config.Text.Layers.Statistical,
                EnableVendorSpecific = req.EnableVendorSpecific ?? _config.Text.Layers.VendorSpecific,
                LlmEndpoint = _config.Text.LlmEndpoint,
                LlmModel = _config.Text.LlmModel,
            };
            TextCleanResult result = await _textPipeline.CleanAsync(req.Text, options, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });

        // POST /detect/text
        app.MapPost("/detect/text", (TextRequest req) =>
        {
            if (string.IsNullOrEmpty(req.Text))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'text' is required."));
            }

            IReadOnlyList<WatermarkMatch> matches = _textPipeline.Detect(req.Text);
            return Results.Ok(matches);
        });

        // POST /clean/markdown
        app.MapPost("/clean/markdown", (MarkdownRequest req) =>
        {
            if (string.IsNullOrEmpty(req.Markdown))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'markdown' is required."));
            }

            MarkdownCleanOptions options = req.StripAll == true ? MarkdownCleanOptions.StripAll() : new MarkdownCleanOptions();
            MarkdownCleanResult result = _markdownCleaner.Clean(req.Markdown, options);
            return Results.Ok(result);
        });

        // POST /clean/file  (multipart upload)
        app.MapPost("/clean/file", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload a file via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            if (!_fileRouter.IsSupported(file.FileName))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.UnsupportedFormat, $"Unsupported file type: {Path.GetExtension(file.FileName)}"));
            }

            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-in-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            string tmpOut = Path.Combine(Path.GetTempPath(), $"wr-out-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                FileCleanResult result = _fileRouter.Clean(tmpIn, tmpOut, new MetadataCleanOptions());
                byte[] bytes = await File.ReadAllBytesAsync(tmpOut, ct).ConfigureAwait(false);
                return Results.File(bytes, "application/octet-stream", $"cleaned-{file.FileName}");
            }
            finally
            {
                TryDelete(tmpIn);
                TryDelete(tmpOut);
            }
        });

        // GET /inspect/file  (multipart upload via POST is more natural, but spec lists inspect under file ops)
        app.MapPost("/inspect/file", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload a file via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            if (!_fileRouter.IsSupported(file.FileName))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.UnsupportedFormat, $"Unsupported file type: {Path.GetExtension(file.FileName)}"));
            }

            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-ins-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                IReadOnlyList<MetadataEntry> entries = _fileRouter.Inspect(tmpIn);
                return Results.Ok(entries);
            }
            finally
            {
                TryDelete(tmpIn);
            }
        });

        // POST /clean/image  (multipart upload)
        app.MapPost("/clean/image", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload an image via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            string ext = Path.GetExtension(file.FileName);
            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-img-in-{Guid.NewGuid():N}{ext}");
            string tmpOut = Path.Combine(Path.GetTempPath(), $"wr-img-out-{Guid.NewGuid():N}{ext}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                ImageCleanOptions options = new()
                {
                    ModelPath = _config.Image.ModelPath,
                    AutoDetectThreshold = _config.Image.AutoDetectThreshold,
                    BlendEdges = _config.Image.BlendEdges,
                };
                ImageCleanResult result = await _imagePipeline.CleanAsync(tmpIn, tmpOut, options, ct).ConfigureAwait(false);
                byte[] bytes = await File.ReadAllBytesAsync(tmpOut, ct).ConfigureAwait(false);
                return Results.File(bytes, "application/octet-stream", $"cleaned-{file.FileName}");
            }
            finally
            {
                TryDelete(tmpIn);
                TryDelete(tmpOut);
            }
        });

        // POST /detect/image  (multipart upload)
        app.MapPost("/detect/image", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload an image via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            string ext = Path.GetExtension(file.FileName);
            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-img-det-{Guid.NewGuid():N}{ext}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                ImageCleanOptions options = new()
                {
                    ModelPath = _config.Image.ModelPath,
                    AutoDetectThreshold = _config.Image.AutoDetectThreshold,
                };
                IReadOnlyList<DetectedRegion> regions = _imagePipeline.Detect(tmpIn, options);
                return Results.Ok(regions);
            }
            finally
            {
                TryDelete(tmpIn);
            }
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private sealed record TextRequest(string Text, bool? EnableUnicode, bool? EnableStatistical, bool? EnableVendorSpecific);

    private sealed record MarkdownRequest(string Markdown, bool? StripAll);
}
