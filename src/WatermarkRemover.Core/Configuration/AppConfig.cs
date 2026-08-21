namespace WatermarkRemover.Core.Configuration;

/// <summary>Root application configuration (deserialized from config.yaml).</summary>
public sealed class AppConfig
{
    public TextConfig Text { get; set; } = new();
    public MarkdownConfig Markdown { get; set; } = new();
    public ImageConfig Image { get; set; } = new();
    public MetadataConfig Metadata { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();

    public static AppConfig Default { get; } = new();
}

public sealed class TextConfig
{
    public TextLayersConfig Layers { get; set; } = new();
    public string LlmEndpoint { get; set; } = "http://localhost:11434";
    public string LlmModel { get; set; } = "llama3";
}

public sealed class TextLayersConfig
{
    public bool Unicode { get; set; } = true;
    public bool Statistical { get; set; }
    public bool VendorSpecific { get; set; } = true;
}

public sealed class MarkdownConfig
{
    // Every property below maps 1:1 to a boolean toggle on
    // WatermarkRemover.Core.Models.MarkdownCleanOptions. The defaults here
    // MUST match the record's defaults — the docs, the config.yaml
    // canonical example, and the xUnit smoke tests all assert that. A
    // drift here means users who delete a key from config.yaml silently
    // get a different behaviour than users who set it explicitly.
    //
    // PreserveCodeBlocks is the only property that does not map to
    // MarkdownCleanOptions directly — it's a legacy CLI knob that
    // operators still expect; the cleaner always preserves fences unless
    // StripCodeFences is on, so the two interact. Kept here for
    // backward compatibility.
    public bool StripHeadings { get; set; } = true;
    public bool StripCodeFences { get; set; }
    public bool StripInlineCode { get; set; }
    public bool StripLinks { get; set; }
    public bool StripImages { get; set; } = true;
    public bool StripBoldItalic { get; set; }
    public bool StripBlockquotes { get; set; }
    public bool StripHr { get; set; } = true;
    public bool StripHtml { get; set; } = true;
    public bool StripComments { get; set; } = true;
    public bool StripTaskLists { get; set; }
    public bool StripTableSyntax { get; set; }
    public bool NormalizeLists { get; set; } = true;
    public bool UnwrapEmptyLists { get; set; } = true;
    public bool StripXmlTags { get; set; } = true;
    public bool StripFrontmatter { get; set; } = true;
    public bool StripAiSignatures { get; set; } = true;
    public bool StripMentions { get; set; } = true;
    public bool StripUnicodeMd { get; set; } = true;
    public bool StripTrailingWs { get; set; } = true;
    public bool ApplyUnicodeLayerA { get; set; } = true;
    public bool PreserveCodeBlocks { get; set; } = true;
}

public sealed class ImageConfig
{
    public string ModelPath { get; set; } = "./models/big_lama_regular_inpaint.onnx";
    public double AutoDetectThreshold { get; set; } = 0.4;
    public bool BlendEdges { get; set; } = true;
}

public sealed class MetadataConfig
{
    public bool StripC2pa { get; set; } = true;
    public bool StripExif { get; set; } = true;
    public bool StripXmp { get; set; } = true;
    public bool PreserveColorProfile { get; set; } = true;
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string Output { get; set; } = "console";
}

/// <summary>
/// HTTP server (currently <c>serve</c>) settings. Holds knobs that
/// only matter when the API host is up — they're safely ignored by
/// every other command.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>Per-IP rate-limit policy for the global limiter.</summary>
    public RateLimitConfig RateLimit { get; set; } = new();

    /// <summary>
    /// Maximum request body size for multipart uploads, in megabytes.
    /// Applies to <c>/clean/file</c>, <c>/clean/image</c>,
    /// <c>/inspect/file</c>, and <c>/detect/image</c>. Oversized
    /// uploads are rejected with HTTP 413 <i>before</i> the body is
    /// streamed to disk. <c>0</c> disables the limit (use Kestrel's
    /// default, 30 MB) — not recommended for public deployments.
    /// </summary>
    public int MaxUploadMB { get; set; } = 100;
}

/// <summary>
/// Token-bucket style rate-limit parameters. Mirrors the
/// <see cref="System.Threading.RateLimiting.FixedWindowRateLimiterOptions"/>
/// shape; <see cref="WindowSeconds"/> is the wall-clock window the
/// counter resets in.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>Number of requests permitted per <see cref="WindowSeconds"/> per partition (typically per remote IP).</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Window length, in seconds, over which <see cref="PermitLimit"/> applies.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Maximum queued requests when the limit is hit. 0 = reject immediately (default).</summary>
    public int QueueLimit { get; set; } = 0;
}

/// <summary>
/// Settings for the <c>serve-mcp</c> command (WR-S11). All fields
/// are intentionally safe-by-default: stdio transport (so a stray
/// remote agent can't accidentally attach), no auth required
/// (auth makes no sense for a localhost stdio pipe), and a port
/// that doesn't collide with <c>serve</c>'s default of 5080.
/// </summary>
public sealed class McpConfig
{
    /// <summary>Transport for the MCP server. <c>stdio</c> is the
    /// default (and the right choice for local agent integrations).
    /// <c>http</c> starts a Streamable HTTP transport so remote
    /// agents can reach the server over the network.</summary>
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>TCP port for the HTTP transport. Ignored for stdio.
    /// Default 5090 — distinct from <c>serve</c>'s 5080 so the two
    /// commands can run side by side without flag-flipping.</summary>
    public int Port { get; set; } = 5090;

    /// <summary>Interface to bind the HTTP transport to. Default
    /// <c>0.0.0.0</c> (all interfaces). Ignored for stdio.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Optional API key for the HTTP transport. When set,
    /// every request must carry the matching <c>X-API-Key</c> header
    /// — same auth pattern as the regular <c>serve</c> command.
    /// <c>null</c> disables auth (typical for localhost dev).
    /// Ignored for stdio (the stdio pipe is the auth boundary).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Per-IP rate-limit policy for the HTTP transport.
    /// Defaults to <c>server.rate_limit</c> when null; an explicit
    /// value here overrides it. Ignored for stdio.</summary>
    public RateLimitConfig? RateLimit { get; set; }
}

/// <summary>
/// Transport selection for the MCP server. The string values are
/// what callers type on the CLI and in <c>config.yaml</c>;
/// <see cref="McpTransportExtensions.Parse"/> normalises the spelling
/// to a known <see cref="McpTransport"/> value.
/// </summary>
public enum McpTransport
{
    /// <summary>Local stdio pipe (default). Used by Claude Code,
    /// OpenCode, MiniMax Code, Cursor, Continue, etc.</summary>
    Stdio,

    /// <summary>Streamable HTTP transport (stateless). Used by
    /// remote agents and Docker deployments.</summary>
    Http,
}

/// <summary>
/// Helpers for converting the textual <c>transport</c> setting on
/// <see cref="McpConfig"/> into a strongly-typed
/// <see cref="McpTransport"/>. Lives next to the enum so any
/// caller — the CLI, the YAML loader, the tests — gets the same
/// "stdio / http / STDIO / HTTP" → enum mapping without
/// duplicating the case-folding logic.
/// </summary>
public static class McpTransportExtensions
{
    /// <summary>
    /// Case-insensitive parse. Returns <c>null</c> for <c>null</c>/
    /// whitespace input (callers fall back to the default), or
    /// throws <see cref="ArgumentException"/> for an unknown
    /// spelling — that way a typo in <c>config.yaml</c> shows up
    /// as a clear error at start-up rather than silently falling
    /// back to stdio.
    /// </summary>
    public static McpTransport Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return McpTransport.Stdio;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "stdio" or "pipe" => McpTransport.Stdio,
            "http" or "streamable" or "streamable-http" or "streamable_http" => McpTransport.Http,
            _ => throw new ArgumentException(
                $"Unknown MCP transport '{value}'. Supported: stdio, http.", nameof(value)),
        };
    }

    /// <summary>Inverse of <see cref="Parse"/> — the canonical
    /// CLI / config.yaml spelling for a transport value.</summary>
    public static string ToConfigString(this McpTransport transport) => transport switch
    {
        McpTransport.Stdio => "stdio",
        McpTransport.Http => "http",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };
}
