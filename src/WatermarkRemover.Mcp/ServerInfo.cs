namespace WatermarkRemover.Mcp;

/// <summary>
/// Constants surfaced to MCP clients during the <c>initialize</c>
/// handshake. Bumping <see cref="ServerVersion"/> in lockstep with
/// <c>WatermarkRemover.CLI</c> keeps the value reported to agents
/// consistent with the CLI's <c>--version</c> output.
/// </summary>
public static class ServerInfo
{
    /// <summary>Server name reported to MCP clients in the initialize response.</summary>
    public const string ServerName = "WatermarkRemover";

    /// <summary>Server version reported to MCP clients in the initialize response.</summary>
    public const string ServerVersion = "1.0.0";
}
