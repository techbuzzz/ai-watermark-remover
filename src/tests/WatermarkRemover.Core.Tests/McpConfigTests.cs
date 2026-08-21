using FluentAssertions;
using WatermarkRemover.Core.Configuration;
using Xunit;

namespace WatermarkRemover.Core.Tests;

/// <summary>
/// Tests for the <see cref="McpConfig"/> defaults and the
/// <see cref="McpTransportExtensions"/> parse helper. Defaults here
/// are load-bearing: they back the <c>mcp:</c> section in
/// <c>config.yaml</c> and the default-mode behaviour of
/// <c>watermarkremover serve-mcp</c>. Drift between the C# defaults
/// and the YAML would mean users who omit a key silently get a
/// different behaviour than users who set it explicitly.
/// </summary>
public sealed class McpConfigTests
{
    [Fact]
    public void Default_Transport_IsStdio()
    {
        // stdio is the safe default — a stray remote agent can't
        // accidentally attach to a port the operator never opened.
        new McpConfig().Transport.Should().Be(McpTransport.Stdio);
    }

    [Fact]
    public void Default_Port_Is5090()
    {
        // 5090 is distinct from `serve`'s 5080 so the two can run
        // side by side without flag-flipping.
        new McpConfig().Port.Should().Be(5090);
    }

    [Fact]
    public void Default_Host_IsAnyInterface()
    {
        new McpConfig().Host.Should().Be("0.0.0.0");
    }

    [Fact]
    public void Default_ApiKey_IsNull()
    {
        // Auth off by default — the stdio pipe is the auth boundary
        // for the default transport, and localhost dev doesn't need
        // a key for the HTTP transport either.
        new McpConfig().ApiKey.Should().BeNull();
    }

    [Fact]
    public void Default_RateLimit_IsNull()
    {
        // When null, the host falls back to the shared server.rate_limit
        // block (see ServeMcpCommand.ResolveRateLimit).
        new McpConfig().RateLimit.Should().BeNull();
    }

    [Fact]
    public void AppConfig_Default_IncludesMcpSection()
    {
        // The mcp: section must be present on the root config so
        // the YAML loader (which ignores unknown keys but doesn't
        // synthesise missing sections) picks it up automatically.
        AppConfig.Default.Mcp.Should().NotBeNull();
    }
}

/// <summary>
/// Tests for the textual-to-enum mapping on
/// <see cref="McpTransportExtensions.Parse"/>. The CLI flag and the
/// YAML key both go through this method, so a typo on either side
/// should produce the same clear error.
/// </summary>
public sealed class McpTransportParseTests
{
    [Theory]
    [InlineData("stdio", McpTransport.Stdio)]
    [InlineData("STDIO", McpTransport.Stdio)]
    [InlineData("Stdio", McpTransport.Stdio)]
    [InlineData("  stdio  ", McpTransport.Stdio)]
    [InlineData("pipe", McpTransport.Stdio)]
    [InlineData("http", McpTransport.Http)]
    [InlineData("HTTP", McpTransport.Http)]
    [InlineData("Http", McpTransport.Http)]
    [InlineData("streamable", McpTransport.Http)]
    [InlineData("streamable-http", McpTransport.Http)]
    [InlineData("streamable_http", McpTransport.Http)]
    public void Parse_AcceptsEverySpelling(string input, McpTransport expected)
    {
        McpTransportExtensions.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_FallsBackToStdio(string? input)
    {
        // A missing key in config.yaml is the most common path to
        // this method — it must silently fall back to the default
        // (stdio) rather than throw, so a user who copies a minimal
        // config.yaml still gets a working server.
        McpTransportExtensions.Parse(input).Should().Be(McpTransport.Stdio);
    }

    [Theory]
    [InlineData("websocket")]
    [InlineData("grpc")]
    [InlineData("sse")]
    [InlineData("tcp")]
    [InlineData("nope")]
    public void Parse_UnknownValue_Throws(string input)
    {
        // A typo should fail loudly at start-up, not silently
        // fall back to stdio (the user would think the HTTP path
        // is up and never see the connection attempts).
        Action act = () => McpTransportExtensions.Parse(input);
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*Unknown MCP transport '{input}'*");
    }

    [Fact]
    public void ToConfigString_Stdio_ReturnsLowercaseSpelling()
    {
        McpTransport.Stdio.ToConfigString().Should().Be("stdio");
    }

    [Fact]
    public void ToConfigString_Http_ReturnsLowercaseSpelling()
    {
        McpTransport.Http.ToConfigString().Should().Be("http");
    }

    [Fact]
    public void Parse_ToConfigString_RoundTrips()
    {
        // Whatever the caller parses, ToConfigString should give
        // back the canonical spelling used in config.yaml / docs.
        foreach (McpTransport t in Enum.GetValues<McpTransport>())
        {
            string canonical = t.ToConfigString();
            McpTransportExtensions.Parse(canonical).Should().Be(t);
        }
    }
}
