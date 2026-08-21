using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Core.Interfaces;

/// <summary>Strips metadata from a single file format.</summary>
public interface IFileMetadataCleaner
{
    /// <summary>File extensions handled by this cleaner (lowercase, with leading dot).</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    bool CanHandle(string extension);

    /// <summary>Strip metadata from <paramref name="inputPath"/>, writing the result to <paramref name="outputPath"/>.</summary>
    FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options);

    /// <summary>Report (without removing) all metadata found in the file.</summary>
    IReadOnlyList<MetadataEntry> Inspect(string inputPath);
}

/// <summary>Routes a file to the correct <see cref="IFileMetadataCleaner"/> based on its extension.</summary>
public interface IFileCleanerRouter
{
    IFileMetadataCleaner? Resolve(string path);
    bool IsSupported(string path);
    IReadOnlyCollection<string> SupportedExtensions { get; }

    FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options);
    IReadOnlyList<MetadataEntry> Inspect(string inputPath);
}
