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
using Microsoft.OpenApi;
using Spectre.Console.Cli;
using Swashbuckle.AspNetCore.SwaggerGen;
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

        [CommandOption("--rate-limit <REQUESTS>")]
        [Description("Override server.rate_limit.permit_limit from config.yaml. " +
                     "Must be > 0. Sets the maximum requests per --rate-window per remote IP.")]
        public int? RateLimit { get; init; }

        [CommandOption("--rate-window <SECONDS>")]
        [Description("Override server.rate_limit.window_seconds from config.yaml. " +
                     "Must be > 0. Window length for the rate-limit counter.")]
        public int? RateWindow { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Resolve rate-limit knobs once, up-front, so we can fail fast on bad input
        // before binding sockets. CLI > config.yaml > built-in default.
        RateLimitConfig rateLimit = ResolveRateLimit(_config.Server.RateLimit, settings.RateLimit, settings.RateWindow);
        if (rateLimit.PermitLimit <= 0 || rateLimit.WindowSeconds <= 0)
        {
            OutputFormatter.Error(
                $"Invalid rate-limit configuration: permit_limit={rateLimit.PermitLimit}, window_seconds={rateLimit.WindowSeconds}. Both must be > 0.");
            return 1;
        }

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
                    PermitLimit = rateLimit.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimit.WindowSeconds),
                    QueueLimit = rateLimit.QueueLimit,
                });
            });
        });

        // OpenAPI / Swagger — machine-readable schema + interactive UI at /swagger.
        // Mounted in dev/test/debug builds; left in place for release too because
        // the docs URL is useful for anyone consuming the API.
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "WatermarkRemover HTTP API",
                Version = "v1",
                Description =
                    "HTTP surface of the `watermarkremover` CLI. " +
                    "Auth: when the server is started with `--api-key`, every endpoint " +
                    "except `/health` requires an `X-API-Key` header. " +
                    "Uploads use `multipart/form-data`; everything else is JSON.",
            });

            // Document the X-API-Key header so it shows up in the UI.
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-API-Key",
                Description = "API key configured with --api-key on the server.",
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", document)] = new List<string>(),
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

        // Serve the OpenAPI spec at /swagger/v1/swagger.json and the interactive
        // UI at /swagger. Mounted before the static-file middleware so the
        // SPA-style fallback below doesn't accidentally swallow the spec.
        app.UseSwagger(c =>
        {
            c.RouteTemplate = "swagger/{documentName}/swagger.{json|yaml}";
        });
        app.UseSwaggerUI(c =>
        {
            c.RoutePrefix = "swagger";
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "WatermarkRemover HTTP API v1");
            c.DocumentTitle = "WatermarkRemover HTTP API";
            c.DisplayRequestDuration();
        });

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
        OutputFormatter.Info(
            $"Rate limit: {rateLimit.PermitLimit} requests / {rateLimit.WindowSeconds}s per IP " +
            $"(queue_limit={rateLimit.QueueLimit}, source={(settings.RateLimit.HasValue || settings.RateWindow.HasValue ? "CLI override" : "config.yaml")}).");

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Resolution order: <c>--rate-limit</c>/<c>--rate-window</c> CLI flags
    /// &gt; <c>server.rate_limit.*</c> from <c>config.yaml</c> &gt; built-in
    /// defaults already baked into <see cref="RateLimitConfig"/>. A null CLI
    /// value means "use the config-side value"; a positive CLI value wins
    /// outright.
    /// </summary>
    private static RateLimitConfig ResolveRateLimit(RateLimitConfig fromConfig, int? cliPermit, int? cliWindow)
    {
        int permit = cliPermit ?? fromConfig.PermitLimit;
        int window = cliWindow ?? fromConfig.WindowSeconds;
        int queue = fromConfig.QueueLimit; // intentionally CLI-only override path; queue is rarely tweaked
        return new RateLimitConfig
        {
            PermitLimit = permit,
            WindowSeconds = window,
            QueueLimit = queue,
        };
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

    /// <summary>JSON body for <c>POST /clean/text</c> and <c>POST /detect/text</c>.</summary>
    /// <remarks>Public so Swashbuckle can reflect on the schema for OpenAPI generation.</remarks>
    public sealed record TextRequest(string Text, bool? EnableUnicode, bool? EnableStatistical, bool? EnableVendorSpecific);

    /// <summary>JSON body for <c>POST /clean/markdown</c>.</summary>
    /// <remarks>Public so Swashbuckle can reflect on the schema for OpenAPI generation.</remarks>
    public sealed record MarkdownRequest(string Markdown, bool? StripAll);
}
