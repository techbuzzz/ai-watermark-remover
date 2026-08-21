using System.ComponentModel;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Image;
using WatermarkRemover.Mcp;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;

namespace WatermarkRemover.CLI.Commands;

/// <summary>
/// Hosts the WatermarkRemover pipeline as an MCP (Model Context Protocol)
/// server so MCP-compatible agents (Claude Code, OpenCode, MiniMax Code,
/// Cursor, Continue, …) can call the eight tools from
/// <c>WatermarkRemover.Mcp</c> directly — no shell-out to the CLI.
/// </summary>
/// <remarks>
/// Two transports are supported, selected via <c>--transport</c>:
/// <list type="bullet">
///   <item><description><b>stdio</b> (default) — local agent integration.
///   The host uses <c>Host.CreateApplicationBuilder()</c>, binds the
///   MCP server to stdin/stdout via <c>WithStdioServerTransport()</c>,
///   and routes <i>all</i> logging to <b>stderr</b> so the stdout
///   stream stays clean for the JSON-RPC protocol.</description></item>
///   <item><description><b>http</b> — Streamable HTTP transport for
///   remote agents. The host uses <c>WebApplication.CreateBuilder()</c>,
///   binds the MCP server via <c>WithHttpTransport(o =&gt; o.Stateless = true)</c>,
///   and mounts the endpoint with <c>app.MapMcp()</c>. Reuses the
///   same auth / port / rate-limit knobs as the regular <c>serve</c>
///   command for consistency.</description></item>
/// </list>
/// <para>
/// <see cref="ExecuteAsync"/> deliberately <i>does not</i> depend on
/// the per-pipeline services it would normally take via DI: when
/// <c>--transport stdio</c> is selected, the agent's <c>stdin</c> is
/// already wired to the host's logger, so a per-instance
/// <c>ILogger&lt;ServeMcpCommand&gt;</c> would still leak to the
/// protocol stream. We wire the pipelines straight into the new
/// host's service collection instead.
/// </para>
/// </remarks>
public sealed class ServeMcpCommand : AsyncCommand<ServeMcpCommand.Settings>
{
    /// <summary>
    /// Settings for the <c>serve-mcp</c> command. Note the absence of
    /// <c>--json</c> and <c>--output</c> from <see cref="GlobalSettings"/>:
    /// both options imply writing to stdout, which is the JSON-RPC
    /// channel when <c>--transport stdio</c> is selected. Inheriting
    /// from <see cref="CommandSettings"/> keeps the user from passing
    /// either by accident.
    /// </summary>
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--transport <TRANSPORT>")]
        [Description("Transport to bind. `stdio` (default) pipes JSON-RPC over stdin/stdout for local agents. `http` exposes a Streamable HTTP endpoint at --host:--port for remote agents.")]
        public string Transport { get; init; } = McpTransport.Stdio.ToConfigString();

        [CommandOption("-H|--host <HOST>")]
        [Description("Interface to bind the HTTP transport to. Ignored for stdio. Default: 0.0.0.0.")]
        public string? Host { get; init; }

        [CommandOption("-p|--port <PORT>")]
        [Description("TCP port for the HTTP transport. Ignored for stdio. Default: 5090.")]
        public int? Port { get; init; }

        [CommandOption("--api-key <KEY>")]
        [Description("Require this API key via the X-API-Key header on every HTTP request. Omit to disable auth. Ignored for stdio.")]
        public string? ApiKey { get; init; }

        [CommandOption("--rate-limit <REQUESTS>")]
        [Description("Override mcp.rate_limit.permit_limit from config.yaml. Must be > 0. Ignored for stdio.")]
        public int? RateLimit { get; init; }

        [CommandOption("--rate-window <SECONDS>")]
        [Description("Override mcp.rate_limit.window_seconds from config.yaml. Must be > 0. Ignored for stdio.")]
        public int? RateWindow { get; init; }
    }

    /// <summary>The resolved config (loaded once in <see cref="Program"/> and
    /// shared with all commands). We only need it to read the
    /// <c>mcp</c> section; the per-pipeline services are wired straight
    /// into the new host so we don't accidentally depend on the
    /// CLI-level DI container for tool resolution.</summary>
    private readonly AppConfig _config;

    /// <summary>Logger for the start-up path (auth, rate-limit, host
    /// binding). Always routed to <b>stderr</b> — even for HTTP — so
    /// the protocol channel stays clean.</summary>
    private readonly ILogger<ServeMcpCommand> _logger;

    public ServeMcpCommand(AppConfig config, ILogger<ServeMcpCommand> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Parse the transport up-front so an unknown value fails with
        // a clear error rather than silently falling back to stdio.
        McpTransport transport;
        try
        {
            transport = McpTransportExtensions.Parse(settings.Transport);
        }
        catch (ArgumentException ex)
        {
            OutputFormatter.Error(ex.Message);
            return 1;
        }

        // Apply the CLI > config.yaml > default precedence for the
        // common knobs. We only consult these for the HTTP path;
        // stdio ignores all of them by design.
        McpConfig mcp = _config.Mcp;
        string host = settings.Host ?? mcp.Host;
        int port = settings.Port ?? mcp.Port;
        string? apiKey = settings.ApiKey ?? mcp.ApiKey;

        // Rate-limit resolution — same pattern ServeCommand uses:
        // CLI > mcp.rate_limit > server.rate_limit > built-in default.
        RateLimitConfig rateLimit = ResolveRateLimit(mcp.RateLimit, _config.Server.RateLimit, settings.RateLimit, settings.RateWindow);
        if (rateLimit.PermitLimit <= 0 || rateLimit.WindowSeconds <= 0)
        {
            OutputFormatter.Error(
                $"Invalid MCP rate-limit configuration: permit_limit={rateLimit.PermitLimit}, window_seconds={rateLimit.WindowSeconds}. Both must be > 0.");
            return 1;
        }

        return transport switch
        {
            McpTransport.Stdio => await RunStdioAsync().ConfigureAwait(false),
            McpTransport.Http => await RunHttpAsync(host, port, apiKey, rateLimit, settings).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown transport: {transport}"),
        };
    }

    /// <summary>
    /// Start the stdio MCP host. The host is built with
    /// <see cref="Host.CreateApplicationBuilder"/> (no Kestrel), and
    /// the MCP server is bound to stdin/stdout via
    /// <c>WithStdioServerTransport</c>. The whole logger pipeline is
    /// configured to <b>stderr only</b> — writing to stdout would
    /// corrupt the JSON-RPC stream that the agent reads.
    /// </summary>
    private async Task<int> RunStdioAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Critical: every log level goes to stderr. The default
        // ConsoleLoggerProvider writes Information+ to stdout, which
        // would corrupt the JSON-RPC stream the agent reads.
        builder.Logging
            .ClearProviders()
            .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Reuse the same wiring the CLI does for the rest of the
        // commands — Core, Text, Metadata, Image — so the tool
        // methods resolve the same pipeline services the rest of
        // the app uses. The MCP-specific registration is a single
        // extension that AddMcpServer + WithToolsFromAssembly.
        builder.Services.AddWatermarkRemoverCore(_config);
        builder.Services.AddWatermarkRemoverText();
        builder.Services.AddWatermarkRemoverMetadata();
        builder.Services.AddWatermarkRemoverImage();
        builder.Services.AddSingleton(_config);
        builder.Services.AddWatermarkRemoverMcp()
            .WithStdioServerTransport();

        IHost host = builder.Build();

        // The "listening on …" line in ServeCommand is intentionally
        // not echoed here: stdout is the JSON-RPC channel and any
        // console output from the host process would be malformed
        // JSON. Operators who need to confirm a stdio server is up
        // can check that the agent registered tools in its UI.
        _logger.LogInformation("WatermarkRemover MCP server starting (stdio transport).");

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Start the Streamable HTTP MCP host. Uses the existing
    /// <c>Microsoft.AspNetCore.App</c> framework reference already on
    /// the CLI project (so no new <c>FrameworkReference</c> is
    /// needed), wires the MCP server via
    /// <c>WithHttpTransport(o =&gt; o.Stateless = true)</c> and mounts
    /// the endpoint with <c>app.MapMcp()</c>. Reuses the same
    /// <c>X-API-Key</c> middleware and per-IP rate-limit pattern as
    /// <see cref="ServeCommand"/>.
    /// </summary>
    private async Task<int> RunHttpAsync(string host, int port, string? apiKey, RateLimitConfig rateLimit, Settings settings)
    {
        if (port <= 0 || port > 65535)
        {
            OutputFormatter.Error($"Invalid MCP port: {port}. Must be 1..65535.");
            return 1;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{host}:{port}");

        builder.Services.AddWatermarkRemoverCore(_config);
        builder.Services.AddWatermarkRemoverText();
        builder.Services.AddWatermarkRemoverMetadata();
        builder.Services.AddWatermarkRemoverImage();
        builder.Services.AddSingleton(_config);
        builder.Services.AddWatermarkRemoverMcp()
            .WithHttpTransport(o => o.Stateless = true);

        // Per-IP rate-limit. The MCP HTTP transport accepts an
        // arbitrary number of concurrent requests (it scales
        // horizontally by spinning up more serve-mcp instances), so
        // a single instance still benefits from a per-IP cap.
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimit.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimit.WindowSeconds),
                        QueueLimit = rateLimit.QueueLimit,
                    }));
        });

        WebApplication app = builder.Build();
        app.UseRateLimiter();

        // API-key middleware — only when a key is configured. The
        // /health endpoint (which MapMcp doesn't auto-add for us, so
        // we just expose it manually below) is exempt so monitoring
        // tooling can scrape it without holding the key.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            string requiredKey = apiKey;
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided) || provided != requiredKey)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new
                    {
                        code = "UNAUTHORIZED",
                        message = "Missing or invalid API key.",
                    }).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok", server = "watermarkremover-mcp" }));
        app.MapMcp();

        OutputFormatter.Success($"WatermarkRemover MCP server listening on http://{host}:{port}");
        OutputFormatter.Info($"MCP endpoint: http://{host}:{port}/ (Streamable HTTP, stateless)");
        OutputFormatter.Info(
            $"Rate limit: {rateLimit.PermitLimit} requests / {rateLimit.WindowSeconds}s per IP " +
            $"(queue_limit={rateLimit.QueueLimit}, source={(settings.RateLimit.HasValue || settings.RateWindow.HasValue ? "CLI override" : "config.yaml")}).");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            OutputFormatter.Warning("API key authentication is ENABLED (X-API-Key header required).");
        }

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Resolution order: <c>--rate-limit</c>/<c>--rate-window</c> CLI flags
    /// &gt; <c>mcp.rate_limit.*</c> from <c>config.yaml</c> &gt;
    /// <c>server.rate_limit.*</c> from <c>config.yaml</c> (the MCP
    /// server inherits the HTTP server's defaults when not configured
    /// directly) &gt; built-in defaults baked into
    /// <see cref="RateLimitConfig"/>. CLI > config > server default —
    /// same precedence as <see cref="ServeCommand"/>.
    /// </summary>
    private static RateLimitConfig ResolveRateLimit(
        RateLimitConfig? mcpConfig,
        RateLimitConfig serverConfig,
        int? cliPermit,
        int? cliWindow)
    {
        RateLimitConfig baseCfg = mcpConfig ?? serverConfig;
        return new RateLimitConfig
        {
            PermitLimit = cliPermit ?? baseCfg.PermitLimit,
            WindowSeconds = cliWindow ?? baseCfg.WindowSeconds,
            QueueLimit = baseCfg.QueueLimit, // intentionally not CLI-overridable; matches ServeCommand
        };
    }
}
