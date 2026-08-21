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
using Microsoft.Extensions.Logging;
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
    AppConfig config,
    ILogger<ServeCommand> logger) : AsyncCommand<ServeCommand.Settings>
{
    private readonly ITextCleaningPipeline _textPipeline = textPipeline;
    private readonly IMarkdownCleaner _markdownCleaner = markdownCleaner;
    private readonly IFileCleanerRouter _fileRouter = fileRouter;
    private readonly IImageCleaningPipeline _imagePipeline = imagePipeline;
    private readonly AppConfig _config = config;
    private readonly ILogger<ServeCommand> _logger = logger;

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

        [CommandOption("--max-upload-mb <MEGABYTES>")]
        [Description("Override server.max_upload_mb from config.yaml. " +
                     "Maximum request body size, in MB, for multipart uploads. " +
                     "0 disables the limit (not recommended for public deployments).")]
        public int? MaxUploadMB { get; init; }
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

        // Upload size limit — rejects oversized multipart bodies *before* they
        // reach the endpoint (and before they're copied to temp files). 0 disables.
        // CLI > config.yaml > built-in default (100 MB).
        int maxUploadMB = settings.MaxUploadMB ?? _config.Server.MaxUploadMB;
        if (maxUploadMB < 0)
        {
            OutputFormatter.Error(
                $"Invalid upload size limit: max_upload_mb={maxUploadMB}. Must be >= 0 (0 disables the limit).");
            return 1;
        }

        long maxUploadBytes = maxUploadMB == 0 ? long.MaxValue : (long)maxUploadMB * 1024 * 1024;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{settings.Host}:{settings.Port}");
        // Lift Kestrel's default 30 MB body cap so our upload-size middleware
        // is the single source of truth for 413 responses (it returns a
        // structured ErrorResult; Kestrel's built-in 413 does not).
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadBytes);

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

        // Upload-size guard. Rejects multipart uploads whose declared
        // Content-Length exceeds the configured limit *before* the body is
        // streamed to temp files. Applies to the four file/image endpoints;
        // everything else passes through unchanged.
        if (maxUploadMB > 0)
        {
            app.Use(async (ctx, next) =>
            {
                if (IsUploadEndpoint(ctx.Request.Path) &&
                    ctx.Request.Headers.TryGetValue("Content-Length", out var lenStr) &&
                    long.TryParse(lenStr, out long len) && len > maxUploadBytes)
                {
                    ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await ctx.Response.WriteAsJsonAsync(new ErrorResult(
                        ErrorCodes.PayloadTooLarge,
                        $"Upload size {len} bytes exceeds the configured limit of {maxUploadMB} MB.")).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        ServeEndpointMapper.MapEndpoints(
            app,
            _textPipeline,
            _markdownCleaner,
            _fileRouter,
            _imagePipeline,
            _config);

        // Serve the OpenAPI spec at /swagger/v1/swagger.json and the interactive
        // UI at /swagger. Mounted before the static-file middleware so the
        // SPA-style fallback below doesn't accidentally swallow the spec.
        ServeEndpointMapper.MountSwagger(app);

        // Bundle the Astro web UI (built by `npm run build` in /web) on the
        // same port. Skipped when the user passes --no-ui or when the
        // wwwroot/ directory wasn't shipped with the binary.
        string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        bool webRootPresent = Directory.Exists(webRoot) &&
                              File.Exists(Path.Combine(webRoot, "index.html"));
        IFileProvider? webRootProvider = webRootPresent
            ? new PhysicalFileProvider(webRoot)
            : null;
        ServeEndpointMapper.MountStaticUi(app, webRootProvider, settings.NoUi, _logger);

        if (webRootProvider is not null)
        {
            OutputFormatter.Success($"Web UI bundle mounted at http://{settings.Host}:{settings.Port}/");
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
        OutputFormatter.Info(
            $"Upload limit: {(maxUploadMB == 0 ? "unlimited" : $"{maxUploadMB} MB")} " +
            $"(source={(settings.MaxUploadMB.HasValue ? "CLI override" : "config.yaml")}).");

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
    /// Returns <c>true</c> for the four multipart upload endpoints that the
    /// upload-size guard applies to.
    /// </summary>
    private static bool IsUploadEndpoint(PathString path) =>
        path.StartsWithSegments("/clean/file", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/clean/image", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/inspect/file", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/detect/image", StringComparison.OrdinalIgnoreCase);

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
}

/// <summary>JSON body for <c>POST /clean/text</c> and <c>POST /detect/text</c>.</summary>
/// <remarks>Top-level so <c>ServeEndpointMapper</c> (and integration tests) can
/// reference it without going through the nested-type path. Public so
/// Swashbuckle can reflect on the schema for OpenAPI generation.</remarks>
public sealed record TextRequest(string Text, bool? EnableUnicode, bool? EnableStatistical, bool? EnableVendorSpecific);

/// <summary>JSON body for <c>POST /clean/markdown</c>.</summary>
/// <remarks>Public so Swashbuckle can reflect on the schema for OpenAPI generation.</remarks>
public sealed record MarkdownRequest(string Markdown, bool? StripAll);
