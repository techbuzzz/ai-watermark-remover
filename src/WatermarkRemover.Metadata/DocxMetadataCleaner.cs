using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips core, extended and custom document properties plus revision history (tracked changes)
/// from DOCX files using the Open XML SDK.
/// </summary>
public sealed class DocxMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".docx"];

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
            using WordprocessingDocument doc = WordprocessingDocument.Open(finalOut, isEditable: true);
            ClearCoreProperties(doc, removed);
            DeleteExtendedProperties(doc, removed);
            DeleteCustomProperties(doc, removed);
            StripRevisionHistory(doc, removed);
            doc.Save();
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to process DOCX: {inputPath}", ex) { FilePath = inputPath };
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
            using WordprocessingDocument doc = WordprocessingDocument.Open(inputPath, isEditable: false);
            var props = doc.PackageProperties;
            AddIfPresent(found, "Core", "Creator", props.Creator);
            AddIfPresent(found, "Core", "Title", props.Title);
            AddIfPresent(found, "Core", "Subject", props.Subject);
            AddIfPresent(found, "Core", "Keywords", props.Keywords);
            AddIfPresent(found, "Core", "Description", props.Description);
            AddIfPresent(found, "Core", "LastModifiedBy", props.LastModifiedBy);
            AddIfPresent(found, "Core", "Category", props.Category);
            AddIfPresent(found, "Core", "Revision", props.Revision);
            if (props.Created.HasValue)
            {
                found.Add(new MetadataEntry("Core", "Created", props.Created.Value.ToString("o")));
            }

            if (props.Modified.HasValue)
            {
                found.Add(new MetadataEntry("Core", "Modified", props.Modified.Value.ToString("o")));
            }

            if (doc.ExtendedFilePropertiesPart is not null)
            {
                found.Add(new MetadataEntry("Extended", "app.xml", "Extended document properties (Company, Application, etc.)"));
            }

            if (doc.CustomFilePropertiesPart is not null)
            {
                found.Add(new MetadataEntry("Custom", "custom.xml", "Custom document properties"));
            }

            int revisions = CountRevisions(doc);
            if (revisions > 0)
            {
                found.Add(new MetadataEntry("Revisions", "TrackedChanges", $"{revisions} tracked change(s)"));
            }
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to inspect DOCX: {inputPath}", ex) { FilePath = inputPath };
        }

        return found;
    }

    private static void ClearCoreProperties(WordprocessingDocument doc, List<MetadataEntry> removed)
    {
        var props = doc.PackageProperties;
        Record(removed, "Core", "Creator", props.Creator);
        Record(removed, "Core", "Title", props.Title);
        Record(removed, "Core", "Subject", props.Subject);
        Record(removed, "Core", "Keywords", props.Keywords);
        Record(removed, "Core", "Description", props.Description);
        Record(removed, "Core", "LastModifiedBy", props.LastModifiedBy);
        Record(removed, "Core", "Category", props.Category);
        Record(removed, "Core", "ContentStatus", props.ContentStatus);
        Record(removed, "Core", "Revision", props.Revision);
        if (props.Created.HasValue)
        {
            removed.Add(new MetadataEntry("Core", "Created", props.Created.Value.ToString("o")));
        }

        if (props.Modified.HasValue)
        {
            removed.Add(new MetadataEntry("Core", "Modified", props.Modified.Value.ToString("o")));
        }

        props.Creator = null;
        props.Title = null;
        props.Subject = null;
        props.Keywords = null;
        props.Description = null;
        props.LastModifiedBy = null;
        props.Category = null;
        props.ContentStatus = null;
        props.Revision = null;
        props.Identifier = null;
        props.Language = null;
        props.Version = null;
        props.Created = null;
        props.Modified = null;
        props.LastPrinted = null;
    }

    private static void DeleteExtendedProperties(WordprocessingDocument doc, List<MetadataEntry> removed)
    {
        if (doc.ExtendedFilePropertiesPart is { } ext)
        {
            removed.Add(new MetadataEntry("Extended", "app.xml", "Deleted extended properties part"));
            doc.DeletePart(ext);
        }
    }

    private static void DeleteCustomProperties(WordprocessingDocument doc, List<MetadataEntry> removed)
    {
        if (doc.CustomFilePropertiesPart is { } custom)
        {
            removed.Add(new MetadataEntry("Custom", "custom.xml", "Deleted custom properties part"));
            doc.DeletePart(custom);
        }
    }

    private static void StripRevisionHistory(WordprocessingDocument doc, List<MetadataEntry> removed)
    {
        MainDocumentPart? main = doc.MainDocumentPart;
        if (main?.Document?.Body is null)
        {
            return;
        }

        int count = 0;

        // Accept insertions: unwrap InsertedRun contents into the parent.
        foreach (InsertedRun ins in main.Document.Descendants<InsertedRun>().ToList())
        {
            var parent = ins.Parent;
            if (parent is null)
            {
                continue;
            }

            foreach (var child in ins.ChildElements.ToList())
            {
                ins.RemoveChild(child);
                parent.InsertBefore(child, ins);
            }

            ins.Remove();
            count++;
        }

        // Accept deletions: remove DeletedRun elements entirely.
        foreach (DeletedRun del in main.Document.Descendants<DeletedRun>().ToList())
        {
            del.Remove();
            count++;
        }

        // Remove moved-content markup.
        foreach (var moved in main.Document.Descendants<MoveFromRun>().ToList())
        {
            moved.Remove();
            count++;
        }

        foreach (MoveToRun moved in main.Document.Descendants<MoveToRun>().ToList())
        {
            var parent = moved.Parent;
            if (parent is null)
            {
                continue;
            }

            foreach (var child in moved.ChildElements.ToList())
            {
                moved.RemoveChild(child);
                parent.InsertBefore(child, moved);
            }

            moved.Remove();
            count++;
        }

        // Remove rsid tracking from the settings part.
        if (main.DocumentSettingsPart?.Settings is { } settings)
        {
            foreach (var rsids in settings.Descendants<Rsids>().ToList())
            {
                rsids.Remove();
            }

            settings.RemoveAllChildren<WriteProtection>();
        }

        if (count > 0)
        {
            removed.Add(new MetadataEntry("Revisions", "TrackedChanges", $"Accepted/removed {count} tracked change(s)"));
        }
    }

    private static int CountRevisions(WordprocessingDocument doc)
    {
        MainDocumentPart? main = doc.MainDocumentPart;
        if (main?.Document is null)
        {
            return 0;
        }

        return main.Document.Descendants<InsertedRun>().Count()
            + main.Document.Descendants<DeletedRun>().Count()
            + main.Document.Descendants<MoveFromRun>().Count()
            + main.Document.Descendants<MoveToRun>().Count();
    }

    private static void Record(List<MetadataEntry> removed, string container, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            removed.Add(new MetadataEntry(container, key, value));
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
