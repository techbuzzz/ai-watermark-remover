using WatermarkRemover.Core.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>Loads <see cref="AppConfig"/> from a YAML file, falling back to defaults.</summary>
public static class ConfigLoader
{
    private static readonly string[] DefaultConfigNames = ["config.yaml", "config.yml"];

    /// <summary>
    /// Load configuration from <paramref name="explicitPath"/> when supplied, otherwise probe the
    /// working directory and executable directory for <c>config.yaml</c>. Returns defaults if none found.
    /// </summary>
    public static AppConfig Load(string? explicitPath)
    {
        string? path = ResolvePath(explicitPath);
        if (path is null || !File.Exists(path))
        {
            return AppConfig.Default;
        }

        try
        {
            string yaml = File.ReadAllText(path);
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            AppConfig? config = deserializer.Deserialize<AppConfig>(yaml);
            return config ?? AppConfig.Default;
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
            // A malformed config should not crash the app; fall back to defaults.
            return AppConfig.Default;
        }
    }

    private static string? ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        foreach (string name in DefaultConfigNames)
        {
            string cwd = Path.Combine(Directory.GetCurrentDirectory(), name);
            if (File.Exists(cwd))
            {
                return cwd;
            }

            string baseDir = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(baseDir))
            {
                return baseDir;
            }
        }

        return null;
    }
}
