using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips core / extended / custom document properties and per-slide
/// comment authorship from PPTX files using the Open XML SDK.
/// </summary>
public sealed class PptxMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".pptx"];

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
        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        if (!string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(finalOut), StringComparison.Ordinal))
        {
            File.Copy(inputPath, finalOut, overwrite: true);
        }

        var removed = new List<MetadataEntry>();
        try
        {
            using PresentationDocument doc = PresentationDocument.Open(finalOut, isEditable: true);
            OpenXmlCoreMetadataCleaner.ClearCoreProperties(doc, removed);
            OpenXmlCoreMetadataCleaner.DeleteExtendedProperties(doc, removed);
            OpenXmlCoreMetadataCleaner.DeleteCustomProperties(doc, removed);
            StripCommentAuthorship(doc, removed);
            doc.Save();
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to process PPTX: {inputPath}", ex) { FilePath = inputPath };
        }

        sw.Stop();
        long outputSize = new FileInfo(finalOut).Length;
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, outputSize, sw.Elapsed);
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
            using PresentationDocument doc = PresentationDocument.Open(inputPath, isEditable: false);
            found.AddRange(OpenXmlCoreMetadataCleaner.InspectCoreProperties(doc));
            if (OpenXmlCoreMetadataCleaner.HasExtendedProperties(doc))
            {
                found.Add(new MetadataEntry("Extended", "app.xml", "Extended presentation properties (Application, Company, etc.)"));
            }

            if (OpenXmlCoreMetadataCleaner.HasCustomProperties(doc))
            {
                found.Add(new MetadataEntry("Custom", "custom.xml", "Custom presentation properties"));
            }

            int slideCommentCount = CountCommentParts(doc);
            if (slideCommentCount > 0)
            {
                found.Add(new MetadataEntry("Comments", "PowerPointCommentPart", $"{slideCommentCount} slide comment part(s)"));
            }

            if (doc.PresentationPart?.authorsPart is not null)
            {
                found.Add(new MetadataEntry("Comments", "PowerPointAuthorsPart", "Presentation-wide author list for comments"));
            }
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to inspect PPTX: {inputPath}", ex) { FilePath = inputPath };
        }

        return found;
    }

    private static void StripCommentAuthorship(PresentationDocument doc, List<MetadataEntry> removed)
    {
        PresentationPart? presPart = doc.PresentationPart;
        if (presPart is null)
        {
            return;
        }

        // Delete every per-slide comment part.
        int slideParts = 0;
        foreach (PowerPointCommentPart part in presPart.commentParts.ToList())
        {
            presPart.DeletePart(part);
            slideParts++;
        }

        if (slideParts > 0)
        {
            removed.Add(new MetadataEntry("Comments", "PowerPointCommentPart", $"Deleted {slideParts} slide comment part(s)"));
        }

        // Delete the presentation-wide author list.
        if (presPart.authorsPart is { } authors)
        {
            presPart.DeletePart(authors);
            removed.Add(new MetadataEntry("Comments", "PowerPointAuthorsPart", "Deleted presentation-wide author list"));
        }
    }

    private static int CountCommentParts(PresentationDocument doc)
    {
        PresentationPart? presPart = doc.PresentationPart;
        if (presPart is null)
        {
            return 0;
        }

        return presPart.commentParts.Count();
    }
}
