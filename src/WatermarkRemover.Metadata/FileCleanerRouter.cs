using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>Routes files to the correct <see cref="IFileMetadataCleaner"/> based on their extension.</summary>
public sealed class FileCleanerRouter : IFileCleanerRouter
{
    private readonly IReadOnlyList<IFileMetadataCleaner> _cleaners;

    public FileCleanerRouter(IEnumerable<IFileMetadataCleaner> cleaners)
    {
        ArgumentNullException.ThrowIfNull(cleaners);
        _cleaners = cleaners.ToList();
        SupportedExtensions = _cleaners
            .SelectMany(c => c.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; }

    public IFileMetadataCleaner? Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return _cleaners.FirstOrDefault(c => c.CanHandle(ext));
    }

    public bool IsSupported(string path) => Resolve(path) is not null;

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        IFileMetadataCleaner cleaner = Resolve(inputPath)
            ?? throw new MetadataStripException($"Unsupported file type: {Path.GetExtension(inputPath)}") { FilePath = inputPath };
        return cleaner.Clean(inputPath, outputPath, options);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        IFileMetadataCleaner cleaner = Resolve(inputPath)
            ?? throw new MetadataStripException($"Unsupported file type: {Path.GetExtension(inputPath)}") { FilePath = inputPath };
        return cleaner.Inspect(inputPath);
    }
}
