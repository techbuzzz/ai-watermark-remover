using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Tests for the Cursor / Continue MCP-config snippets and the npm
/// install section shipped in <c>docs/MCP.md</c>. The acceptance gate
/// from WR-S18 is "Cursor + Continue config snippets work" — these
/// tests parse the embedded JSON blocks out of the Markdown and
/// verify they are well-formed and wire to the right shape for each
/// host. Catches drift between the doc and the actual config spec.
/// </summary>
/// <remarks>
/// Cursor and Continue both consume the same MCP spec but their
/// config files are subtly different:
///   - Cursor: <c>~/.cursor/mcp.json</c>, top-level <c>mcpServers</c>
///     as an object keyed by server name.
///   - Continue: <c>~/.continue/config.json</c>, top-level
///     <c>mcpServers</c> as an **array** of objects, each with its
///     own <c>name</c> field.
/// We assert both shapes so a future doc edit can't quietly break
/// the integration.
/// </remarks>
public sealed class CursorContinueConfigTests
{
    private const string McpDocRelativePath = "docs/MCP.md";
    private const string McpServerName = "watermarkremover";
    private const string NpmPackageSpec = "@watermarkremover/mcp";

    [Fact]
    public void McpDoc_Exists()
    {
        File.Exists(LocateMcpDoc()).Should().BeTrue();
    }

    [Fact]
    public void CursorSection_Header_IsPresent()
    {
        string doc = File.ReadAllText(LocateMcpDoc());
        // Match a Markdown line that starts with 2-4 `#` followed by
        // whitespace and `Cursor` (Multiline so `^` matches the start
        // of any line, not just the start of the file).
        System.Text.RegularExpressions.Regex.IsMatch(
                doc,
                @"^#{2,4}\s+Cursor\b",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Should()
            .BeTrue("the MCP doc must contain a 'Cursor' install section");
    }

    [Fact]
    public void ContinueSection_Header_IsPresent()
    {
        string doc = File.ReadAllText(LocateMcpDoc());
        System.Text.RegularExpressions.Regex.IsMatch(
                doc,
                @"^#{2,4}\s+Continue\b",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Should()
            .BeTrue("the MCP doc must contain a 'Continue' install section");
    }

    [Fact]
    public void NpmSection_Header_IsPresent()
    {
        string doc = File.ReadAllText(LocateMcpDoc());
        // Look for a dedicated `### npm package (@watermarkremover/mcp)`
        // header. The `### ` prefix is intentional — the npm section
        // is a sibling of Cursor / Continue / Docker under `## Install`.
        System.Text.RegularExpressions.Regex.IsMatch(
                doc,
                @"^#{2,4}\s+npm\s+package\b",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Should()
            .BeTrue("the MCP doc must contain a dedicated 'npm package' install section");
        doc.Should().Contain(NpmPackageSpec, "the MCP doc must reference the @watermarkremover/mcp npm wrapper");
    }

    [Fact]
    public void CursorConfig_IsValidJson_AndRegistersMcpServer()
    {
        string cursorJson = ExtractCursorSnippet(File.ReadAllText(LocateMcpDoc()));
        Action act = () => JsonDocument.Parse(cursorJson);
        act.Should().NotThrow("the Cursor snippet must be valid JSON");

        using JsonDocument doc = JsonDocument.Parse(cursorJson);
        // Cursor: `mcpServers` is an **object** keyed by server name.
        JsonElement servers = doc.RootElement.GetProperty("mcpServers");
        servers.ValueKind.Should().Be(JsonValueKind.Object, "Cursor uses an object for mcpServers");
        servers.TryGetProperty(McpServerName, out JsonElement entry).Should().BeTrue();
        ValidateStdServerEntry(entry, allowStdioCommand: true);
    }

    [Fact]
    public void ContinueConfig_IsValidJson_AndRegistersMcpServer()
    {
        string continueJson = ExtractContinueSnippet(File.ReadAllText(LocateMcpDoc()));
        Action act = () => JsonDocument.Parse(continueJson);
        act.Should().NotThrow("the Continue snippet must be valid JSON");

        using JsonDocument doc = JsonDocument.Parse(continueJson);
        // Continue: `mcpServers` is an **array** of objects, each
        // carrying its own `name`.
        JsonElement servers = doc.RootElement.GetProperty("mcpServers");
        servers.ValueKind.Should().Be(JsonValueKind.Array, "Continue uses an array for mcpServers");

        JsonElement match = servers.EnumerateArray()
            .FirstOrDefault(e => e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("name", out JsonElement n)
                && n.GetString() == McpServerName);

        match.ValueKind.Should().NotBe(JsonValueKind.Undefined, "the Continue snippet must include a server entry with name 'watermarkremover'");
        ValidateStdServerEntry(match, allowStdioCommand: true);
    }

    [Fact]
    public void NpmConfig_ForCursor_IsValidJson_AndRegistersMcpServer()
    {
        string snippet = ExtractNpmCursorSnippet(File.ReadAllText(LocateMcpDoc()));
        snippet.Should().NotBeNullOrWhiteSpace("the MCP doc must include a Cursor + npm install example");

        using JsonDocument doc = JsonDocument.Parse(snippet);
        JsonElement servers = doc.RootElement.GetProperty("mcpServers");
        servers.ValueKind.Should().Be(JsonValueKind.Object);
        JsonElement entry = servers.GetProperty(McpServerName);
        ValidateNpxServerEntry(entry);
    }

    [Fact]
    public void NpmConfig_ForContinue_IsValidJson_AndRegistersMcpServer()
    {
        string snippet = ExtractNpmContinueSnippet(File.ReadAllText(LocateMcpDoc()));
        snippet.Should().NotBeNullOrWhiteSpace("the MCP doc must include a Continue + npm install example");

        using JsonDocument doc = JsonDocument.Parse(snippet);
        JsonElement servers = doc.RootElement.GetProperty("mcpServers");
        servers.ValueKind.Should().Be(JsonValueKind.Array);

        JsonElement match = servers.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == McpServerName);
        ValidateNpxServerEntry(match);
    }

    // ---- helpers --------------------------------------------------------

    private static void ValidateStdServerEntry(JsonElement entry, bool allowStdioCommand)
    {
        entry.GetProperty("command").GetString().Should().NotBeNullOrEmpty();
        JsonElement.ArrayEnumerator args = entry.GetProperty("args").EnumerateArray();
        args.Should().NotBeEmpty("the entry must pass at least one argument");
        // Either `watermarkremover` on PATH or `npx …` works.
        string command = entry.GetProperty("command").GetString()!;
        command.Should().Match(c =>
            c == "watermarkremover" || c == "npx" || c.EndsWith("/watermarkremover", StringComparison.Ordinal),
            "the snippet's command should be `watermarkremover`, `npx`, or an absolute path to the binary");

        bool hasServeMcp = false;
        foreach (JsonElement arg in args)
        {
            if (arg.GetString() == "serve-mcp")
            {
                hasServeMcp = true;
                break;
            }
        }
        hasServeMcp.Should().BeTrue("the args must include `serve-mcp` so the binary actually starts the MCP server");
    }

    private static void ValidateNpxServerEntry(JsonElement entry)
    {
        entry.GetProperty("command").GetString().Should().Be("npx");
        JsonElement.ArrayEnumerator args = entry.GetProperty("args").EnumerateArray();
        List<string> argList = new();
        foreach (JsonElement arg in args)
        {
            argList.Add(arg.GetString() ?? string.Empty);
        }
        argList.Should().Contain(NpmPackageSpec, "the npx command must reference the @watermarkremover/mcp package");
    }

    /// <summary>
    /// Extract the first ```json fenced block that appears between the
    /// 'Cursor' header and the next sibling '### ' or '## ' header.
    /// </summary>
    private static string ExtractCursorSnippet(string doc)
    {
        return ExtractSnippetAfterHeader(doc, headerPrefix: "### Cursor", fallbackPrefix: "## Cursor");
    }

    private static string ExtractContinueSnippet(string doc)
    {
        return ExtractSnippetAfterHeader(doc, headerPrefix: "### Continue", fallbackPrefix: "## Continue");
    }

    /// <summary>
    /// Locate the dedicated `### npm package (...)` section header
    /// and return the body until the next sibling `##` or `###`
    /// header.
    /// </summary>
    private static string ExtractNpmSection(string doc)
    {
        System.Text.RegularExpressions.Match header = System.Text.RegularExpressions.Regex.Match(
            doc,
            @"^#{2,4}\s+npm\s+package\b[^\n]*",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        header.Success.Should().BeTrue("a dedicated `### npm package` section must exist in the MCP doc");

        int bodyStart = header.Index + header.Length;
        int nextHeader = NextHeader(doc, bodyStart);
        int end = nextHeader < 0 ? doc.Length : nextHeader;
        return doc.Substring(header.Index, end - header.Index);
    }

    private static string ExtractNpmCursorSnippet(string doc)
    {
        string npmSection = ExtractNpmSection(doc);
        return FirstJsonBlock(npmSection);
    }

    private static string ExtractNpmContinueSnippet(string doc)
    {
        string npmSection = ExtractNpmSection(doc);
        // The second JSON block typically covers Continue.
        return NthJsonBlock(npmSection, index: 1);
    }

    private static string ExtractSnippetAfterHeader(string doc, string headerPrefix, string fallbackPrefix)
    {
        int headerIdx = doc.IndexOf(headerPrefix, StringComparison.Ordinal);
        if (headerIdx < 0)
        {
            headerIdx = doc.IndexOf(fallbackPrefix, StringComparison.Ordinal);
        }
        headerIdx.Should().BeGreaterThan(0, $"expected a '{headerPrefix}' (or '{fallbackPrefix}') header in the doc");

        int nextHeader = NextHeader(doc, headerIdx + headerPrefix.Length);
        string section = doc.Substring(headerIdx, nextHeader < 0 ? doc.Length - headerIdx : nextHeader - headerIdx);
        string json = FirstJsonBlock(section);
        json.Should().NotBeNullOrWhiteSpace($"expected a ```json fenced block in the '{headerPrefix}' section");
        return json;
    }

    private static int NextHeader(string doc, int start)
    {
        for (int i = start; i < doc.Length - 2; i++)
        {
            if (doc[i] == '#' && doc[i + 1] == '#' && (i == 0 || doc[i - 1] == '\n'))
            {
                return i;
            }
        }
        return -1;
    }

    private static string FirstJsonBlock(string text)
    {
        return NthJsonBlock(text, index: 0);
    }

    private static string NthJsonBlock(string text, int index)
    {
        // Only match ```json fences (NOT ```jsonc, ```json5, etc.) so
        // the snippet is guaranteed to be strict JSON that the host
        // can parse.
        int cursor = 0;
        int found = 0;
        while (true)
        {
            int fenceOpen = FindJsonFence(text, cursor);
            if (fenceOpen < 0)
            {
                return string.Empty;
            }
            int bodyStart = fenceOpen + 7; // length of "```json"
            int lineEnd = text.IndexOf('\n', bodyStart);
            if (lineEnd < 0)
            {
                return string.Empty;
            }
            int body = lineEnd + 1;
            int fenceClose = text.IndexOf("```", body, StringComparison.Ordinal);
            if (fenceClose < 0)
            {
                return string.Empty;
            }
            if (found == index)
            {
                return text.Substring(body, fenceClose - body).Trim();
            }
            found++;
            cursor = fenceClose + 3;
        }
    }

    /// <summary>
    /// Locate the next ```json fence that is **not** followed by an
    /// alphanumeric character. ```jsonc, ```json5, ```jsonlines and
    /// similar are skipped so the extracted snippet is always strict
    /// JSON.
    /// </summary>
    private static int FindJsonFence(string text, int start)
    {
        int cursor = start;
        while (cursor < text.Length)
        {
            int found = text.IndexOf("```json", cursor, StringComparison.Ordinal);
            if (found < 0)
            {
                return -1;
            }
            int after = found + 7;
            if (after >= text.Length || !IsAsciiAlpha(text[after]))
            {
                return found;
            }
            cursor = after;
        }
        return -1;
    }

    private static bool IsAsciiAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static string LocateMcpDoc()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, McpDocRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"Could not locate '{McpDocRelativePath}' walking up from {AppContext.BaseDirectory}.");
    }
}
