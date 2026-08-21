namespace WatermarkRemover.Core.Models;

/// <summary>Result of stripping metadata from a file.</summary>
public record FileCleanResult(
    string InputPath,
    string OutputPath,
    IReadOnlyList<MetadataEntry> RemovedEntries,
    long InputSizeBytes,
    long OutputSizeBytes,
    TimeSpan ProcessingTime
);

/// <summary>A single metadata entry (either removed or discovered during inspection).</summary>
public record MetadataEntry(string Container, string Key, string Value);
