namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>Helpers for resolving text input (arg / file / stdin) and writing text output.</summary>
public static class IoHelper
{
    /// <summary>
    /// Resolve text input from, in priority order: an explicit <paramref name="inputFile"/>,
    /// a positional <paramref name="positional"/> value, or piped stdin.
    /// </summary>
    public static async Task<string> ReadTextAsync(string? positional, string? inputFile, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(inputFile))
        {
            return await File.ReadAllTextAsync(inputFile, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(positional))
        {
            return positional;
        }

        if (Console.IsInputRedirected)
        {
            using StreamReader reader = new(Console.OpenStandardInput());
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return string.Empty;
    }

    /// <summary>Write text either to <paramref name="outputPath"/> (when set) or stdout.</summary>
    public static async Task WriteTextAsync(string? outputPath, string content, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(outputPath, content, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Console.Out.WriteAsync(content).ConfigureAwait(false);
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
        }
    }
}
