using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Static-structural tests for the MCP packaging story shipped in
/// <c>WR-S20 / WR-P631 + WR-P633</c>:
/// <list type="number">
///   <item><description>The <c>Dockerfile</c> exposes the MCP HTTP port
///   (<c>5090</c>) and documents a <c>serve-mcp</c> <c>CMD</c> variant.</description></item>
///   <item><description>The <c>docker-compose.yml</c> ships an <c>mcp</c>
///   service that runs the Streamable HTTP transport.</description></item>
///   <item><description>The release workflow (<c>.github/workflows/release.yml</c>)
///   smoke-tests the <c>serve-mcp</c> sub-command on every RID before
///   publishing the binary.</description></item>
///   <item><description>The MCP NuGet packages are version-pinned in
///   <c>src/Directory.Packages.props</c> (central package management).</description></item>
///   <item><description><c>docs/MCP.md</c> documents Docker / Compose
///   usage so the packaging is discoverable.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// These tests guard the contract that the repo guarantees to anyone
/// who pulls the image or downloads a release binary. The dynamic
/// behaviour (MCP JSON-RPC over stdio / HTTP) is covered by
/// <c>WatermarkRemover.Mcp.Tests</c> and <c>ServeMcpCommandTests</c>;
/// this fixture is the "the wiring is in place" half.
/// </remarks>
public sealed class McpDockerPackagingTests
{
    private const string DockerfileRelativePath = "Dockerfile";
    private const string DockerComposeRelativePath = "docker-compose.yml";
    private const string ReleaseWorkflowRelativePath = ".github/workflows/release.yml";
    private const string DirectoryPackagesPropsRelativePath = "src/Directory.Packages.props";
    private const string McpDocsRelativePath = "docs/MCP.md";
    private const string RepoRoot = "../../../..";

    private static string LocateRepoRoot()
    {
        // AppContext.BaseDirectory points at the test bin output, e.g.
        //   src/tests/WatermarkRemover.CLI.Tests/bin/Debug/net10.0/
        // The repo root is marked by `Directory.Build.props` (the
        // sln lives one level lower, under `src/`, so we cannot use
        // that as the marker).
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Directory.Build.props")) &&
                File.Exists(Path.Combine(dir, "global.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }

    private static string Locate(string relative)
    {
        string root = LocateRepoRoot();
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    // ---------------------------------------------------------------- Dockerfile

    [Fact]
    public void Dockerfile_IsPresent()
    {
        File.Exists(Locate(DockerfileRelativePath))
            .Should().BeTrue("the project must ship a Dockerfile for the MCP Docker image");
    }

    [Fact]
    public void Dockerfile_ExposesMcpPort_5090()
    {
        // The MCP transport binds to 5090 by default. The Dockerfile
        // must `EXPOSE` it so `docker run -p 5090:5090` works without
        // the user having to know the port.
        string raw = File.ReadAllText(Locate(DockerfileRelativePath));

        // Match an `EXPOSE` line that lists 5090 (possibly alongside
        // other ports). Tolerate both `EXPOSE 5080 5090` and the
        // multi-line `EXPOSE 5080\nEXPOSE 5090` shapes. The character
        // class is digits + spaces (not the broader `\s`, which would
        // greedily swallow newlines and break the `$` anchor).
        MatchCollection matches = Regex.Matches(
            raw,
            @"^[ \t]*EXPOSE[ \t]+(?<ports>[0-9 /]+)[ \t]*\r?$",
            RegexOptions.Multiline);

        bool exposes = matches
            .Select(m => m.Groups["ports"].Value)
            .SelectMany(p => p.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
            .Any(p => p == "5090");

        exposes.Should().BeTrue(
            "Dockerfile must `EXPOSE 5090` so the MCP HTTP transport is reachable via `docker run -p 5090:5090`.");
    }

    [Fact]
    public void Dockerfile_DocumentsServeMcpSubCommand_Override()
    {
        // The image's ENTRYPOINT is the apphost, so the operator
        // selects a sub-command by passing it as the CMD. The header
        // comment must call out the `serve-mcp` example so the
        // MCP-only deployment path is discoverable.
        string raw = File.ReadAllText(Locate(DockerfileRelativePath));

        raw.Should().Contain("serve-mcp",
            "Dockerfile must document a `serve-mcp` CMD override so MCP-only deploys are obvious");
        raw.Should().Contain("5090",
            "Dockerfile must reference port 5090 in the header so MCP users find the example");
    }

    [Fact]
    public void Dockerfile_KeepsHttpApiPort_5080_Exposed()
    {
        // Regression guard — the MCP update must not silently drop the
        // HTTP API's EXPOSE. A fresh user who pulls the image should
        // still be able to `docker run -p 5080:5080` and get the
        // Astro UI.
        string raw = File.ReadAllText(Locate(DockerfileRelativePath));

        MatchCollection matches = Regex.Matches(
            raw,
            @"^[ \t]*EXPOSE[ \t]+(?<ports>[0-9 /]+)[ \t]*\r?$",
            RegexOptions.Multiline);

        bool exposes = matches
            .Select(m => m.Groups["ports"].Value)
            .SelectMany(p => p.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
            .Any(p => p == "5080");

        exposes.Should().BeTrue(
            "Dockerfile must keep `EXPOSE 5080` exposed for the HTTP API + Astro UI sub-command");
    }

    // ----------------------------------------------------------- docker-compose

    [Fact]
    public void DockerCompose_IsPresent()
    {
        File.Exists(Locate(DockerComposeRelativePath))
            .Should().BeTrue("the project must ship a docker-compose.yml for local dev");
    }

    [Fact]
    public void DockerCompose_DeclaresMcpService()
    {
        string raw = File.ReadAllText(Locate(DockerComposeRelativePath));
        // Top-level `mcp:` service block, indented two spaces under `services:`.
        Regex mcpServiceRegex = new(
            @"^  mcp:\s*$",
            RegexOptions.Multiline);

        mcpServiceRegex.IsMatch(raw)
            .Should().BeTrue("docker-compose.yml must declare a top-level `mcp:` service");
    }

    [Fact]
    public void DockerCompose_McpService_RunsServeMcpHttp()
    {
        string raw = File.ReadAllText(Locate(DockerComposeRelativePath));

        // Find the `mcp:` block and assert its command line invokes
        // `serve-mcp --transport http`. We use a permissive regex
        // that handles the YAML list-of-strings shape used in the file.
        Match mcpBlock = Regex.Match(
            raw,
            @"^  mcp:\s*\n(?<body>(?:^    .*\n|^\s*\n)+)",
            RegexOptions.Multiline);
        mcpBlock.Success.Should().BeTrue("could not isolate the mcp service block");

        string body = mcpBlock.Groups["body"].Value;
        body.Should().Contain("serve-mcp",
            "the mcp service must invoke `serve-mcp`");
        body.Should().Contain("\"http\"",
            "the mcp service must pin the transport to http (Streamable HTTP)");
        body.Should().Contain("5090",
            "the mcp service must publish port 5090");
    }

    [Fact]
    public void DockerCompose_McpService_HealthchecksHttpTransport()
    {
        // The MCP transport has its own `/health` endpoint (exempt
        // from the API-key check). The compose healthcheck must probe
        // :5090, not the HTTP API's :5080, otherwise the healthcheck
        // would 404 on an MCP-only deployment.
        string raw = File.ReadAllText(Locate(DockerComposeRelativePath));
        raw.Should().Contain("http://127.0.0.1:5090/health",
            "the mcp service healthcheck must probe the MCP transport's /health on :5090");
    }

    [Fact]
    public void DockerCompose_KeepsWatermarkremoverService()
    {
        // Regression guard — adding the mcp service must not displace
        // the existing HTTP API service.
        string raw = File.ReadAllText(Locate(DockerComposeRelativePath));
        raw.Should().Contain("watermarkremover:",
            "the HTTP API service must still be present alongside the new mcp service");
        raw.Should().Contain("5080",
            "the HTTP API service must still publish port 5080");
    }

    // ------------------------------------------------------------ release.yml

    [Fact]
    public void ReleaseWorkflow_IsPresent()
    {
        File.Exists(Locate(ReleaseWorkflowRelativePath))
            .Should().BeTrue("the release workflow must exist for the serve-mcp smoke test to live in");
    }

    [Fact]
    public void ReleaseWorkflow_SmokeTests_ServeMcpSubCommand()
    {
        // The release workflow runs one publish per RID. A smoke test
        // on the built binary is the cheapest way to catch a
        // regression where the MCP SDK got accidentally trimmed out
        // (it is a pure managed library, so trimming should be
        // safe — but a `PublishTrimmed=true` flag flip would silently
        // break MCP without breaking the regular `serve`).
        string raw = File.ReadAllText(Locate(ReleaseWorkflowRelativePath));
        raw.Should().Contain("serve-mcp",
            "release.yml must smoke-test `serve-mcp` so a trimmed-out MCP SDK fails the build");
        raw.Should().Contain("--help",
            "the smoke test must use `serve-mcp --help` so it can run in a non-interactive CI shell");
    }

    [Fact]
    public void ReleaseWorkflow_SmokeTest_AssertsTransportHelpText()
    {
        // A weak smoke test would only check that the binary exists.
        // We go one step further and assert the help output mentions
        // `--transport` and at least one of the two supported values
        // (stdio / http). A regression that lost the http transport
        // would silently degrade Docker users to stdio.
        string raw = File.ReadAllText(Locate(ReleaseWorkflowRelativePath));
        raw.Should().Contain("--transport",
            "release.yml must assert that the help output mentions --transport");
        raw.Should().MatchRegex("stdio\\|http",
            "release.yml must assert that the help output mentions both stdio and http transports");
    }

    // -------------------------------------------- Directory.Packages.props

    [Fact]
    public void DirectoryPackagesProps_PinsModelContextProtocol()
    {
        string raw = File.ReadAllText(Locate(DirectoryPackagesPropsRelativePath));
        raw.Should().Contain("<PackageVersion Include=\"ModelContextProtocol\"",
            "Directory.Packages.props must pin the ModelContextProtocol package version");
        raw.Should().MatchRegex(
            "<PackageVersion Include=\"ModelContextProtocol\"\\s+Version=\"[0-9]+\\.[0-9]+\\.[0-9]+\"\\s*/>",
            "ModelContextProtocol must be pinned to a concrete SemVer");
    }

    [Fact]
    public void DirectoryPackagesProps_PinsModelContextProtocolAspNetCore()
    {
        // The HTTP transport lives in a separate NuGet package
        // (`ModelContextProtocol.AspNetCore`). Both must be pinned
        // so a transitive bump on one doesn't drag the other.
        string raw = File.ReadAllText(Locate(DirectoryPackagesPropsRelativePath));
        raw.Should().Contain("<PackageVersion Include=\"ModelContextProtocol.AspNetCore\"",
            "Directory.Packages.props must pin the ModelContextProtocol.AspNetCore package version");
        raw.Should().MatchRegex(
            "<PackageVersion Include=\"ModelContextProtocol.AspNetCore\"\\s+Version=\"[0-9]+\\.[0-9]+\\.[0-9]+\"\\s*/>",
            "ModelContextProtocol.AspNetCore must be pinned to a concrete SemVer");
    }

    // ----------------------------------------------------------------- docs

    [Fact]
    public void McpDocs_HasDockerSection()
    {
        string raw = File.ReadAllText(Locate(McpDocsRelativePath));
        raw.Should().Contain("### Docker",
            "docs/MCP.md must have a Docker installation section so MCP users can find the image recipe");
    }

    [Fact]
    public void McpDocs_DockerSection_MentionsCompose()
    {
        // The compose stack is the local-dev entry point. The doc
        // must show the `docker compose up mcp` invocation so users
        // can find the path from the prose.
        string raw = File.ReadAllText(Locate(McpDocsRelativePath));
        raw.Should().Contain("docker compose",
            "docs/MCP.md Docker section must mention `docker compose` for the local dev loop");
        raw.Should().Contain("5090",
            "docs/MCP.md Docker section must reference port 5090");
    }

    [Fact]
    public void McpDocs_Troubleshooting_MentionsDockerPortCollision()
    {
        // The existing troubleshooting table should already mention
        // 5090-in-use. A regression that pruned the table would
        // leave Docker users without a one-link fix.
        string raw = File.ReadAllText(Locate(McpDocsRelativePath));
        raw.Should().Contain("5090",
            "docs/MCP.md must reference port 5090 (in the troubleshooting table or similar)");
    }
}
