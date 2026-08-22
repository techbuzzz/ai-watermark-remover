using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips core / extended / custom document properties and comment
/// authorship (workbook-level authors + per-worksheet threaded comments)
/// from XLSX files using the Open XML SDK.
/// </summary>
public sealed class XlsxMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".xlsx"];

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
            using SpreadsheetDocument doc = SpreadsheetDocument.Open(finalOut, isEditable: true);
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
            throw new MetadataStripException($"Failed to process XLSX: {inputPath}", ex) { FilePath = inputPath };
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
            using SpreadsheetDocument doc = SpreadsheetDocument.Open(inputPath, isEditable: false);
            found.AddRange(OpenXmlCoreMetadataCleaner.InspectCoreProperties(doc));
            if (OpenXmlCoreMetadataCleaner.HasExtendedProperties(doc))
            {
                found.Add(new MetadataEntry("Extended", "app.xml", "Extended workbook properties (Application, Company, etc.)"));
            }

            if (OpenXmlCoreMetadataCleaner.HasCustomProperties(doc))
            {
                found.Add(new MetadataEntry("Custom", "custom.xml", "Custom workbook properties"));
            }

            int commentCount = CountCommentParts(doc);
            if (commentCount > 0)
            {
                found.Add(new MetadataEntry("Comments", "WorksheetComments+ThreadedComments", $"{commentCount} comment / threaded-comment part(s)"));
            }

            if (HasWorkbookAuthorsPart(doc))
            {
                found.Add(new MetadataEntry("Comments", "CommentAuthorsPart", "Workbook-wide author list for threaded comments"));
            }
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to inspect XLSX: {inputPath}", ex) { FilePath = inputPath };
        }

        return found;
    }

    private static void StripCommentAuthorship(SpreadsheetDocument doc, List<MetadataEntry> removed)
    {
        WorkbookPart? wbPart = doc.WorkbookPart;
        if (wbPart is null)
        {
            return;
        }

        int worksheetCommentParts = 0;
        int worksheetThreadedCommentParts = 0;

        foreach (WorksheetPart wsPart in wbPart.WorksheetParts.ToList())
        {
            if (wsPart.WorksheetCommentsPart is { } comments)
            {
                wsPart.DeletePart(comments);
                worksheetCommentParts++;
            }

            foreach (WorksheetThreadedCommentsPart threaded in wsPart.WorksheetThreadedCommentsParts.ToList())
            {
                wsPart.DeletePart(threaded);
                worksheetThreadedCommentParts++;
            }
        }

        if (worksheetCommentParts > 0)
        {
            removed.Add(new MetadataEntry("Comments", "WorksheetCommentsPart", $"Deleted {worksheetCommentParts} worksheet comment part(s)"));
        }

        if (worksheetThreadedCommentParts > 0)
        {
            removed.Add(new MetadataEntry("Comments", "WorksheetThreadedCommentsPart", $"Deleted {worksheetThreadedCommentParts} threaded comment part(s)"));
        }

        // The workbook-wide authors list is not surfaced as a strongly-typed accessor on
        // WorkbookPart in OpenXml SDK 3.x; find it via the parts collection.
        List<CommentAuthorsPart> authorParts = wbPart.Parts.Select(p => p.OpenXmlPart).OfType<CommentAuthorsPart>().ToList();
        foreach (CommentAuthorsPart part in authorParts)
        {
            wbPart.DeletePart(part);
        }

        if (authorParts.Count > 0)
        {
            removed.Add(new MetadataEntry("Comments", "CommentAuthorsPart", $"Deleted {authorParts.Count} workbook-wide author list part(s)"));
        }
    }

    private static int CountCommentParts(SpreadsheetDocument doc)
    {
        WorkbookPart? wbPart = doc.WorkbookPart;
        if (wbPart is null)
        {
            return 0;
        }

        int count = 0;
        foreach (WorksheetPart wsPart in wbPart.WorksheetParts)
        {
            if (wsPart.WorksheetCommentsPart is not null)
            {
                count++;
            }

            count += wsPart.WorksheetThreadedCommentsParts.Count();
        }

        return count;
    }

    private static bool HasWorkbookAuthorsPart(SpreadsheetDocument doc)
    {
        WorkbookPart? wbPart = doc.WorkbookPart;
        if (wbPart is null)
        {
            return false;
        }

        return wbPart.Parts.Select(p => p.OpenXmlPart).OfType<CommentAuthorsPart>().Any();
    }
}

