using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Structural validation tests for the V1 local MiniMax Code plugin
/// package shipped under <c>minimax-code/watermark-remover/</c>. The
/// package must conform to the V1 local plugin spec — manifest at
/// <c>.minimax-plugin/plugin.json</c>, all required fields present,
/// every referenced file resolvable, every skill frontmatter
/// <c>name</c> matching its directory, icon bytes a real PNG, etc.
/// </summary>
/// <remarks>
/// The reference is the
/// <a href="https://modelcontextprotocol.io">MCP / MiniMax plugin
/// docs</a>; the canonical shape is the
/// <c>plugin-creator</c> skill in MiniMax Code's local runtime. The
/// test here only checks what the runtime can verify statically —
/// the actual <c>tools/list</c> handshake is covered by the
/// <c>WatermarkRemover.Mcp.Tests</c> fixture.
/// </remarks>
public sealed class MinimaxCodePluginManifestTests
{
    private const string PluginRelativeDir = "minimax-code/watermark-remover";
    private const string ManifestRelativePath = ".minimax-plugin/plugin.json";
    private const string McpConfigRelativePath = "servers.mcp.json";
    private const string IconRelativePath = "icon.png";
    private const string ReadmeRelativePath = "README.md";

    private static readonly string[] ValidCategories =
    {
        "Office",
        "Studio",
        "Design & Sites",
        "Code",
        "Business",
        "Sales",
        "Productivity",
        "Science & Healthcare",
        "Education",
        "Other",
    };

    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled);

    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.Compiled);

    [Fact]
    public void PluginFolder_IsPresent()
    {
        string pluginDir = LocatePluginDir();
        Directory.Exists(pluginDir)
            .Should()
            .BeTrue($"expected MiniMax Code plugin folder at {pluginDir}");
    }

    [Fact]
    public void Manifest_IsValidJson()
    {
        string manifestPath = LocateManifestPath();
        string raw = File.ReadAllText(manifestPath);

        // Reject BOMs and trailing junk up front.
        Action act = () => JsonDocument.Parse(raw);
        act.Should().NotThrow($"the manifest at {manifestPath} must be valid JSON");
    }

    [Fact]
    public void Manifest_HasAllRequiredFields()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        JsonElement root = doc.RootElement;

        root.TryGetProperty("schemaVersion", out _).Should().BeTrue("schemaVersion is required");
        root.TryGetProperty("name", out _).Should().BeTrue("name is required");
        root.TryGetProperty("version", out _).Should().BeTrue("version is required");
        root.TryGetProperty("description", out _).Should().BeTrue("description is required");
        root.TryGetProperty("author", out _).Should().BeTrue("author is required");
        root.TryGetProperty("icon", out _).Should().BeTrue("icon is required");
        root.TryGetProperty("category", out _).Should().BeTrue("category is required");
        root.TryGetProperty("exampleQueries", out _).Should().BeTrue("exampleQueries is required");
        root.TryGetProperty("apps", out _).Should().BeTrue("apps is required");
        root.TryGetProperty("mcpServers", out _).Should().BeTrue("mcpServers is required");
        root.TryGetProperty("skills", out _).Should().BeTrue("skills is required");
    }

    [Fact]
    public void Manifest_SchemaVersion_IsOne()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Manifest_Name_IsValidKebabCase()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string name = doc.RootElement.GetProperty("name").GetString() ?? string.Empty;
        name.Should().NotBeEmpty();
        NamePattern.IsMatch(name)
            .Should()
            .BeTrue($"name '{name}' must match {NamePattern}");
        // The plugin name must match the directory name.
        string pluginDir = LocatePluginDir();
        Path.GetFileName(pluginDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Should()
            .Be(name, "plugin directory name must equal manifest 'name' per the V1 spec");
    }

    [Fact]
    public void Manifest_Version_IsSemVer()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string version = doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
        SemVerPattern.IsMatch(version)
            .Should()
            .BeTrue($"version '{version}' must be SemVer");
    }

    [Fact]
    public void Manifest_Category_IsOneOfTheAcceptedValues()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string category = doc.RootElement.GetProperty("category").GetString() ?? string.Empty;
        ValidCategories.Should().Contain(category);
    }

    [Fact]
    public void Manifest_Description_IsNonEmpty()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string description = doc.RootElement.GetProperty("description").GetString() ?? string.Empty;
        description.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void Manifest_ExampleQueries_AreAllNonEmpty()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        JsonElement queries = doc.RootElement.GetProperty("exampleQueries");
        queries.ValueKind.Should().Be(JsonValueKind.Array);
        queries.GetArrayLength().Should().BeGreaterThan(0);

        foreach (JsonElement q in queries.EnumerateArray())
        {
            string text = q.GetString() ?? string.Empty;
            text.Trim().Should().NotBeEmpty("every example query must contain non-whitespace text");
        }
    }

    [Fact]
    public void Manifest_Apps_IsEmptyArray()
    {
        // Local App capability is not effective in V1 — must be [].
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        JsonElement apps = doc.RootElement.GetProperty("apps");
        apps.ValueKind.Should().Be(JsonValueKind.Array);
        apps.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Manifest_HasAtLeastOneEffectiveCapability()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        JsonElement root = doc.RootElement;
        int mcpServers = root.GetProperty("mcpServers").GetArrayLength();
        int skills = root.GetProperty("skills").GetArrayLength();
        (mcpServers + skills).Should().BeGreaterThan(0, "the package must declare at least one mcpServers or skills entry");
    }

    [Fact]
    public void Manifest_NoUnknownFields()
    {
        // The V1 spec says the manifest is a JSON object with no unknown fields.
        // We don't enforce this against a hard-coded list (that would be brittle
        // across spec versions), but we do flag any field that looks like a
        // secret / credential / OAuth blob — those are explicitly rejected by
        // the spec.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        HashSet<string> forbidden = new(StringComparer.OrdinalIgnoreCase)
        {
            "auth", "oauth", "client_id", "client_secret", "token", "apiKey", "api_key",
        };

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            forbidden.Should().NotContain(prop.Name, $"manifest field '{prop.Name}' is not allowed by the V1 spec");
        }
    }

    [Fact]
    public void Manifest_AllMcpConfigPaths_Exist()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string pluginDir = LocatePluginDir();

        foreach (JsonElement element in doc.RootElement.GetProperty("mcpServers").EnumerateArray())
        {
            string rel = element.GetString() ?? string.Empty;
            rel.Should().NotBeEmpty("every mcpServers entry must be a non-empty relative path");
            Path.IsPathRooted(rel).Should().BeFalse("paths in the manifest must be relative (no drive letter or leading /)");
            rel.Should().NotStartWith("..", "paths must not escape the package root");
            string abs = Path.Combine(pluginDir, rel.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(abs).Should().BeTrue($"manifest references missing MCP config at {abs}");
        }
    }

    [Fact]
    public void Manifest_AllSkillPaths_Exist()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string pluginDir = LocatePluginDir();

        foreach (JsonElement element in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            string rel = element.GetString() ?? string.Empty;
            rel.Should().NotBeEmpty("every skills entry must be a non-empty relative path");
            rel.Should().StartWith("skills/", "skill paths must start with 'skills/'");
            rel.Should().EndWith("/SKILL.md", "skill paths must end with '/SKILL.md'");
            Path.IsPathRooted(rel).Should().BeFalse();
            rel.Should().NotStartWith("..");
            string abs = Path.Combine(pluginDir, rel.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(abs).Should().BeTrue($"manifest references missing skill at {abs}");
        }
    }

    [Fact]
    public void Manifest_IconFile_Exists()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string iconRel = doc.RootElement.GetProperty("icon").GetString() ?? string.Empty;
        iconRel.Should().NotBeEmpty("icon must reference a non-empty path");
        Path.IsPathRooted(iconRel).Should().BeFalse("icon must be a relative path");
        string iconAbs = Path.Combine(LocatePluginDir(), iconRel.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(iconAbs).Should().BeTrue($"icon file missing at {iconAbs}");
    }

    [Fact]
    public void Manifest_IconFile_IsPng()
    {
        // Validate the bytes — the first 8 bytes of a PNG are the
        // 0x89 P N G \r \n 0x1A \n signature. This catches "icon got
        // truncated to zero bytes", "wrong file copied", "binary was
        // base64-encoded", etc.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        string iconRel = doc.RootElement.GetProperty("icon").GetString() ?? string.Empty;
        string iconAbs = Path.Combine(LocatePluginDir(), iconRel.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(iconAbs).Should().BeTrue();

        byte[] bytes = File.ReadAllBytes(iconAbs);
        bytes.Length.Should().BeGreaterThan(8);
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50, "byte 1 must be 'P'");
        bytes[2].Should().Be(0x4E, "byte 2 must be 'N'");
        bytes[3].Should().Be(0x47, "byte 3 must be 'G'");
        bytes[4].Should().Be(0x0D);
        bytes[5].Should().Be(0x0A);
        bytes[6].Should().Be(0x1A);
        bytes[7].Should().Be(0x0A);
    }

    [Fact]
    public void PluginFolder_NoPathEscapesRoot()
    {
        // The V1 spec rejects any package path that escapes the
        // package root. We check the package layout defensively — every
        // entry must live under the package root.
        string pluginDir = LocatePluginDir();
        string fullRoot = Path.GetFullPath(pluginDir);

        foreach (string path in Directory.EnumerateFileSystemEntries(pluginDir, "*", SearchOption.AllDirectories))
        {
            string full = Path.GetFullPath(path);
            full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                .Should()
                .BeTrue($"entry {full} escapes the package root {fullRoot}");
        }
    }

    [Fact]
    public void PluginFolder_WithinSizeLimits()
    {
        // V1 spec: at most 1024 regular files, 64 MiB total, 16 MiB
        // per file, 2048 total entries (including directories).
        string pluginDir = LocatePluginDir();
        int fileCount = 0;
        long totalBytes = 0;
        int totalEntries = 0;

        foreach (string path in Directory.EnumerateFileSystemEntries(pluginDir, "*", SearchOption.AllDirectories))
        {
            totalEntries++;
            if (File.Exists(path))
            {
                fileCount++;
                FileInfo info = new(path);
                totalBytes += info.Length;
                info.Length.Should().BeLessThanOrEqualTo(16 * 1024 * 1024, $"{path} exceeds 16 MiB single-file cap");
            }
        }

        fileCount.Should().BeLessThanOrEqualTo(1024, "V1 package limit is 1024 regular files");
        totalEntries.Should().BeLessThanOrEqualTo(2048, "V1 package limit is 2048 total entries");
        totalBytes.Should().BeLessThanOrEqualTo(64L * 1024 * 1024, "V1 package limit is 64 MiB total");
    }

    [Fact]
    public void PluginFolder_NoSymlinks()
    {
        // V1 spec rejects symlinks. DirectoryInfo.LinkTarget is null
        // for non-reparse points on Windows; on Linux we'd need to
        // check the mode bits. The cross-platform check is to inspect
        // the FileAttributes — reparse points on Windows are flagged
        // as ReparsePoint.
        string pluginDir = LocatePluginDir();
        foreach (string path in Directory.EnumerateFileSystemEntries(pluginDir, "*", SearchOption.AllDirectories))
        {
            FileAttributes attrs = File.GetAttributes(path);
            (attrs & FileAttributes.ReparsePoint).Should().Be(0, $"{path} is a reparse point / symlink");
        }
    }

    [Fact]
    public void PluginFolder_HasReadme()
    {
        // A plugin README is not strictly required by the V1 spec, but
        // is a strong UX expectation — every shipped plugin in the
        // MiniMax Code docs has one. Catches accidental deletion.
        File.Exists(Path.Combine(LocatePluginDir(), ReadmeRelativePath))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void McpConfig_IsValidJson()
    {
        string path = Path.Combine(LocatePluginDir(), McpConfigRelativePath);
        File.Exists(path).Should().BeTrue();

        Action act = () => JsonDocument.Parse(File.ReadAllText(path));
        act.Should().NotThrow();
    }

    [Fact]
    public void McpConfig_HasSchemaVersionAndMcpServers()
    {
        string path = Path.Combine(LocatePluginDir(), McpConfigRelativePath);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        doc.RootElement.TryGetProperty("schemaVersion", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("mcpServers", out JsonElement servers).Should().BeTrue();
        servers.ValueKind.Should().Be(JsonValueKind.Object);
        servers.EnumerateObject().Should().NotBeEmpty("at least one MCP server must be declared");
    }

    [Fact]
    public void McpConfig_AllServers_UseSupportedTransport()
    {
        string path = Path.Combine(LocatePluginDir(), McpConfigRelativePath);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (JsonProperty server in doc.RootElement.GetProperty("mcpServers").EnumerateObject())
        {
            string? type = server.Value.GetProperty("type").GetString();
            type.Should().BeOneOf("stdio", "streamable-http", "sse");

            // Name must match the kebab-case regex.
            NamePattern.IsMatch(server.Name)
                .Should()
                .BeTrue($"MCP server name '{server.Name}' must match {NamePattern}");

            if (type == "stdio")
            {
                string command = server.Value.GetProperty("command").GetString() ?? string.Empty;
                command.Should().NotBeEmpty();
                command.Should().NotContain("/").And.NotContain("\\", "stdio command must be PATH-resolved, not a slashed path");
            }
            else
            {
                string url = server.Value.GetProperty("url").GetString() ?? string.Empty;
                url.Should().NotBeEmpty();
                (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    .Should()
                    .BeTrue($"remote MCP server '{server.Name}' must have an http(s) URL");
            }

            // The V1 spec rejects auth / OAuth / embedded credentials.
            HashSet<string> forbidden = new(StringComparer.OrdinalIgnoreCase)
            {
                "auth", "oauth", "client_id", "client_secret", "token", "apiKey", "api_key",
            };
            foreach (JsonProperty prop in server.Value.EnumerateObject())
            {
                forbidden.Should().NotContain(prop.Name, $"MCP server '{server.Name}' field '{prop.Name}' is not allowed by the V1 spec");
            }
        }
    }

    [Fact]
    public void Skills_EachFrontmatterName_MatchesItsDirectory()
    {
        // V1 spec: "The frontmatter `name` must equal `<skill-name>`,
        // and `description` must be non-empty." Where `<skill-name>` is
        // the directory under `skills/`.
        string skillsDir = Path.Combine(LocatePluginDir(), "skills");
        Directory.Exists(skillsDir).Should().BeTrue();

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocateManifestPath()));
        foreach (JsonElement element in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            string rel = element.GetString() ?? string.Empty;
            string abs = Path.Combine(skillsDir, rel.Substring("skills/".Length).Replace('/', Path.DirectorySeparatorChar));
            File.Exists(abs).Should().BeTrue();

            string content = File.ReadAllText(abs);
            (content, string frontmatter) = ExtractFrontmatter(content);
            string? name = GetFrontmatterField(frontmatter, "name");
            string? description = GetFrontmatterField(frontmatter, "description");

            string expectedName = Path.GetFileName(Path.GetDirectoryName(abs)!)!;
            name.Should().Be(expectedName, $"skill at {rel} has frontmatter name '{name}', expected '{expectedName}'");
            description.Should().NotBeNullOrEmpty($"skill at {rel} is missing the 'description' field");
        }
    }

    [Fact]
    public void Commands_Files_PresentForForwardLookingSlashCommands()
    {
        // The V1 manifest doesn't surface `commands/`, but the project
        // ships three forward-looking slash-command files there for
        // any future MiniMax Code version that auto-discovers them.
        // Catches accidental deletion.
        string commandsDir = Path.Combine(LocatePluginDir(), "commands");
        Directory.Exists(commandsDir).Should().BeTrue();

        string[] expected =
        {
            "wr-clean-text.md",
            "wr-clean-file.md",
            "wr-detect.md",
        };

        foreach (string name in expected)
        {
            File.Exists(Path.Combine(commandsDir, name))
                .Should()
                .BeTrue($"forward-looking slash command file {name} is missing");
        }
    }

    // ---- helpers --------------------------------------------------------

    private static string LocatePluginDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, PluginRelativeDir);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            $"Could not locate '{PluginRelativeDir}' walking up from {AppContext.BaseDirectory}.");
    }

    private static string LocateManifestPath()
    {
        return Path.Combine(LocatePluginDir(), ManifestRelativePath);
    }

    /// <summary>
    /// Split a markdown file into (body, frontmatter) where frontmatter
    /// is the YAML between the leading <c>---</c> fences, if any. If
    /// there is no frontmatter, frontmatter is the empty string.
    /// </summary>
    private static (string Body, string Frontmatter) ExtractFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return (content, string.Empty);
        }

        int end = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return (content, string.Empty);
        }

        string frontmatter = content.Substring(3, end - 3);
        string body = content.Substring(end + 3);
        return (body, frontmatter);
    }

    /// <summary>
    /// Pull a single field out of a YAML frontmatter block. This is a
    /// deliberately minimal parser — it handles the shapes we ship
    /// (<c>name: value</c> and <c>name: &gt;\n  body</c>). For
    /// anything more complex, use a real YAML library.
    /// </summary>
    private static string? GetFrontmatterField(string frontmatter, string key)
    {
        if (string.IsNullOrEmpty(frontmatter))
        {
            return null;
        }

        string[] lines = frontmatter.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (!line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }

            string value = line.Substring(key.Length + 1).Trim();
            if (value == ">" || value == "|" || value == ">-" || value == "|-")
            {
                // Folded / literal scalar — collect indented continuation lines.
                var sb = new System.Text.StringBuilder();
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string cont = lines[j].TrimEnd('\r');
                    if (cont.Length == 0 || (!char.IsWhiteSpace(cont[0]) && cont[0] != '-'))
                    {
                        break;
                    }

                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(cont.Trim());
                }

                return sb.ToString();
            }

            // Strip surrounding quotes.
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value.Substring(1, value.Length - 2);
            }

            return value;
        }

        return null;
    }
}
