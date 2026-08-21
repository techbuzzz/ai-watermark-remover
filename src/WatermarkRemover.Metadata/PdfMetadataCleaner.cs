using System.Diagnostics;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips the document information dictionary and XMP metadata streams from PDF files by
/// rebuilding the document from its pages (using PdfPig) without carrying over metadata.
/// </summary>
public sealed class PdfMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".pdf"];

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) => Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        var sw = Stopwatch.StartNew();
        long inputSize = new FileInfo(inputPath).Length;
        var removed = new List<MetadataEntry>();
        byte[] rebuilt;

        try
        {
            using PdfDocument document = PdfDocument.Open(inputPath);
            CollectMetadata(document, removed);

            var builder = new PdfDocumentBuilder { IncludeDocumentInformation = false };
            for (int i = 1; i <= document.NumberOfPages; i++)
            {
                builder.AddPage(document, i);
            }

            rebuilt = builder.Build();
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to process PDF: {inputPath}", ex) { FilePath = inputPath };
        }

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        File.WriteAllBytes(finalOut, rebuilt);
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, rebuilt.LongLength, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        var found = new List<MetadataEntry>();
        try
        {
            using PdfDocument document = PdfDocument.Open(inputPath);
            CollectMetadata(document, found);
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to inspect PDF: {inputPath}", ex) { FilePath = inputPath };
        }

        return found;
    }

    private static void CollectMetadata(PdfDocument document, List<MetadataEntry> entries)
    {
        var info = document.Information;
        AddIfPresent(entries, "Info", "Title", info.Title);
        AddIfPresent(entries, "Info", "Author", info.Author);
        AddIfPresent(entries, "Info", "Subject", info.Subject);
        AddIfPresent(entries, "Info", "Keywords", info.Keywords);
        AddIfPresent(entries, "Info", "Creator", info.Creator);
        AddIfPresent(entries, "Info", "Producer", info.Producer);
        AddIfPresent(entries, "Info", "CreationDate", info.CreationDate);
        AddIfPresent(entries, "Info", "ModifiedDate", info.ModifiedDate);

        if (document.TryGetXmpMetadata(out var xmp) && xmp is not null)
        {
            entries.Add(new MetadataEntry("XMP", "XMP", "Embedded XMP metadata packet"));
        }
    }

    private static void AddIfPresent(List<MetadataEntry> entries, string container, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(container, key, value));
        }
    }
}
