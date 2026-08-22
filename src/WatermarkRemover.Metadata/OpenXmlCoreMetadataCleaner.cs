using DocumentFormat.OpenXml.Packaging;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Shared Open XML metadata cleaner. The three Microsoft Open XML
/// formats (WordprocessingML/DOCX, PresentationML/PPTX, SpreadsheetML/XLSX)
/// all expose the same three metadata parts in their package's <see cref="OpenXmlPartContainer.Parts"/>
/// collection:
/// <list type="bullet">
///   <item><c>docProps/core.xml</c>  — <c>CoreFilePropertiesPart</c> mirrored through <c>PackageProperties</c> (creator, title, lastModifiedBy, …)</item>
///   <item><c>docProps/app.xml</c>   — <c>ExtendedFilePropertiesPart</c> (Application, Company, Manager, …)</item>
///   <item><c>docProps/custom.xml</c> — <c>CustomFilePropertiesPart</c> (user-defined properties)</item>
/// </list>
/// This helper mutates all three on the supplied package via its
/// <c>Parts</c> collection, so it works against the common
/// <see cref="OpenXmlPackage"/> base class without needing
/// per-document-type accessors. The format-specific cleaners
/// (DOCX/PPTX/XLSX) call into it and then add their own format-specific
/// cleanup (revision history, slide comments, threaded comments, …).
/// </summary>
internal static class OpenXmlCoreMetadataCleaner
{
    /// <summary>
    /// Zero out every <see cref="OpenXmlPackage.PackageProperties"/> field
    /// and record what was removed.
    /// </summary>
    public static void ClearCoreProperties(OpenXmlPackage package, List<MetadataEntry> removed)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(removed);

        var props = package.PackageProperties;
        Record(removed, "Core", "Creator", props.Creator);
        Record(removed, "Core", "Title", props.Title);
        Record(removed, "Core", "Subject", props.Subject);
        Record(removed, "Core", "Keywords", props.Keywords);
        Record(removed, "Core", "Description", props.Description);
        Record(removed, "Core", "LastModifiedBy", props.LastModifiedBy);
        Record(removed, "Core", "Category", props.Category);
        Record(removed, "Core", "ContentStatus", props.ContentStatus);
        Record(removed, "Core", "Revision", props.Revision);
        Record(removed, "Core", "Identifier", props.Identifier);
        Record(removed, "Core", "Language", props.Language);
        Record(removed, "Core", "Version", props.Version);
        if (props.Created.HasValue)
        {
            removed.Add(new MetadataEntry("Core", "Created", props.Created.Value.ToString("o")));
        }

        if (props.Modified.HasValue)
        {
            removed.Add(new MetadataEntry("Core", "Modified", props.Modified.Value.ToString("o")));
        }

        if (props.LastPrinted.HasValue)
        {
            removed.Add(new MetadataEntry("Core", "LastPrinted", props.LastPrinted.Value.ToString("o")));
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

    /// <summary>Delete the <c>docProps/app.xml</c> extended-properties part (if present).</summary>
    public static void DeleteExtendedProperties(OpenXmlPackage package, List<MetadataEntry> removed)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(removed);

        List<ExtendedFilePropertiesPart> parts = package.Parts.Select(p => p.OpenXmlPart).OfType<ExtendedFilePropertiesPart>().ToList();
        foreach (ExtendedFilePropertiesPart part in parts)
        {
            package.DeletePart(part);
        }

        if (parts.Count > 0)
        {
            removed.Add(new MetadataEntry("Extended", "app.xml", $"Deleted {parts.Count} extended properties part(s)"));
        }
    }

    /// <summary>Delete the <c>docProps/custom.xml</c> custom-properties part (if present).</summary>
    public static void DeleteCustomProperties(OpenXmlPackage package, List<MetadataEntry> removed)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(removed);

        List<CustomFilePropertiesPart> parts = package.Parts.Select(p => p.OpenXmlPart).OfType<CustomFilePropertiesPart>().ToList();
        foreach (CustomFilePropertiesPart part in parts)
        {
            package.DeletePart(part);
        }

        if (parts.Count > 0)
        {
            removed.Add(new MetadataEntry("Custom", "custom.xml", $"Deleted {parts.Count} custom properties part(s)"));
        }
    }

    /// <summary>Inspect (without removing) every <see cref="OpenXmlPackage.PackageProperties"/> field.</summary>
    public static IEnumerable<MetadataEntry> InspectCoreProperties(OpenXmlPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var props = package.PackageProperties;
        var entries = new List<MetadataEntry>();
        AddIfPresent(entries, "Core", "Creator", props.Creator);
        AddIfPresent(entries, "Core", "Title", props.Title);
        AddIfPresent(entries, "Core", "Subject", props.Subject);
        AddIfPresent(entries, "Core", "Keywords", props.Keywords);
        AddIfPresent(entries, "Core", "Description", props.Description);
        AddIfPresent(entries, "Core", "LastModifiedBy", props.LastModifiedBy);
        AddIfPresent(entries, "Core", "Category", props.Category);
        AddIfPresent(entries, "Core", "ContentStatus", props.ContentStatus);
        AddIfPresent(entries, "Core", "Revision", props.Revision);
        AddIfPresent(entries, "Core", "Identifier", props.Identifier);
        AddIfPresent(entries, "Core", "Language", props.Language);
        AddIfPresent(entries, "Core", "Version", props.Version);
        if (props.Created.HasValue)
        {
            entries.Add(new MetadataEntry("Core", "Created", props.Created.Value.ToString("o")));
        }

        if (props.Modified.HasValue)
        {
            entries.Add(new MetadataEntry("Core", "Modified", props.Modified.Value.ToString("o")));
        }

        if (props.LastPrinted.HasValue)
        {
            entries.Add(new MetadataEntry("Core", "LastPrinted", props.LastPrinted.Value.ToString("o")));
        }

        return entries;
    }

    /// <summary>True when the package carries a <c>docProps/app.xml</c> part.</summary>
    public static bool HasExtendedProperties(OpenXmlPackage package)
        => package.Parts.Select(p => p.OpenXmlPart).OfType<ExtendedFilePropertiesPart>().Any();

    /// <summary>True when the package carries a <c>docProps/custom.xml</c> part.</summary>
    public static bool HasCustomProperties(OpenXmlPackage package)
        => package.Parts.Select(p => p.OpenXmlPart).OfType<CustomFilePropertiesPart>().Any();

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

