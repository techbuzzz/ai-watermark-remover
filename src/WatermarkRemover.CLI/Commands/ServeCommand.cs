using System.ComponentModel;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

    /// <summary>Default CORS origins when the API runs open (no --api-key).</summary>
    private const string DefaultCorsOriginsOpen = "*";

    /// <summary>Default CORS origins when the API is key-protected.</summary>
    private const string DefaultCorsOriginsKeyed = "http://localhost:4321,http://localhost:5080";

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

        [CommandOption("--cors-origins <ORIGINS>")]
        [Description("Comma-separated CORS origin list. '*' for any. Defaults to '*' " +
                     "when --api-key is unset, otherwise 'http://localhost:4321,http://localhost:5080'. " +
                     "Override via WATERMARKREMOVER_CORS_ORIGINS env var or this flag.")]
        public string? CorsOrigins { get; init; }

        [CommandOption("--no-ui")]
        [Description("Skip serving the bundled Astro web UI (wwwroot/) even when present. " +
                     "Useful for headless API-only deployments.")]
        public bool NoUi { get; init; }
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

        // CORS — only enabled when the user (or env var) provides origins. The
        // browser UI needs to call the API cross-origin in dev (Astro's dev
        // server runs on :4321 by default).
        string[] corsOrigins = ResolveCorsOrigins(settings);
        bool corsEnabled = corsOrigins.Length > 0;
        if (corsEnabled)
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    if (corsOrigins.Length == 1 && corsOrigins[0] == "*")
                    {
                        policy.AllowAnyOrigin();
                    }
                    else
                    {
                        policy.WithOrigins(corsOrigins);
                    }
                    policy.AllowAnyHeader().AllowAnyMethod();
                });
            });
        }

        WebApplication app = builder.Build();
        app.UseRateLimiter();
        if (corsEnabled)
        {
            app.UseCors();
        }

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

        // Bundle the Astro web UI (built by `npm run build` in /web) on the
        // same port. Skipped when the user passes --no-ui or when the
        // wwwroot/ directory wasn't shipped with the binary.
        string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        bool webRootPresent = Directory.Exists(webRoot) &&
                              File.Exists(Path.Combine(webRoot, "index.html"));
        if (!settings.NoUi && webRootPresent)
        {
            // Serve index.html on directory hits, static files for everything else.
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                DefaultFileNames = { "index.html" },
                FileProvider = new PhysicalFileProvider(webRoot),
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(webRoot),
            });
            // SPA-style fallback: any non-API path that didn't match a static
            // file or an API route returns index.html so client-side tab
            // routing (and direct deep links like /#file) keep working.
            app.MapFallback(() => Results.File(Path.Combine(webRoot, "index.html"), "text/html"));
            OutputFormatter.Success($"Web UI bundle mounted at http://{settings.Host}:{settings.Port}/");
        }
        else if (!settings.NoUi)
        {
            OutputFormatter.Warning(
                "Web UI bundle not found (expected wwwroot/index.html next to the binary). " +
                "Run `npm run build` in /web to bundle it, or pass --no-ui to silence this warning.");
        }

        OutputFormatter.Success($"WatermarkRemover API listening on http://{settings.Host}:{settings.Port}");
        if (corsEnabled)
        {
            OutputFormatter.Info($"CORS enabled for: {(corsOrigins.Length == 1 && corsOrigins[0] == "*" ? "*" : string.Join(", ", corsOrigins))}");
        }
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            OutputFormatter.Warning("API key authentication is ENABLED (X-API-Key header required).");
        }

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Resolution order: --cors-origins flag > WATERMARKREMOVER_CORS_ORIGINS env var
    /// > smart default based on whether --api-key is set.
    /// Returns an empty array when the user wants CORS off (empty flag value).
    /// </summary>
    private static string[] ResolveCorsOrigins(Settings settings)
    {
        string? raw = settings.CorsOrigins;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable("WATERMARKREMOVER_CORS_ORIGINS");
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = string.IsNullOrWhiteSpace(settings.ApiKey)
                ? DefaultCorsOriginsOpen
                : DefaultCorsOriginsKeyed;
        }
        // Allow the user to explicitly turn CORS off with the empty string after
        // the equals sign: --cors-origins=""
        if (settings.CorsOrigins == string.Empty)
        {
            return Array.Empty<string>();
        }
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
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
