using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace WatermarkRemover.Mcp;

/// <summary>
/// DI registration helpers for the MCP server. The host project (see
/// WR-S11 — <c>serve-mcp</c> in <c>WatermarkRemover.CLI</c>) calls
/// <see cref="AddWatermarkRemoverMcp"/> on the same service collection
/// that already has <c>AddWatermarkRemoverCore / Text / Metadata / Image</c>
/// wired in, so the tool methods resolve every pipeline from the
/// shared DI graph.
/// </summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MCP server builder, scans the
    /// <see cref="WatermarkRemover.Mcp"/> assembly for tool types via
    /// <c>WithToolsFromAssembly</c>, and applies the standard
    /// <see cref="ServerInfo"/> metadata so every client sees the
    /// same <c>serverInfo.name</c> and <c>serverInfo.version</c>.
    /// </summary>
    public static IMcpServerBuilder AddWatermarkRemoverMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddMcpServer(options =>
        {
            options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = ServerInfo.ServerName,
                Version = ServerInfo.ServerVersion,
            };
        })
        // No transport is bound here — the host (serve-mcp stdio or
        // serve-mcp http) calls .WithStdioServerTransport() or
        // .WithHttpTransport() on the builder before RunAsync. We
        // deliberately split the two so this assembly stays
        // transport-agnostic and the stdio / HTTP choice is the
        // host's call.
        .WithToolsFromAssembly();
    }
}
