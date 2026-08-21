using System.Diagnostics;
using HtmlAgilityPack;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips metadata &lt;meta&gt; tags (author, generator, copyright, description, keywords, Dublin Core),
/// XMP blocks and RDF/DC nodes from HTML files using HtmlAgilityPack.
/// </summary>
public sealed class HtmlMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".html", ".htm", ".xhtml"];

    /// <summary>Meta <c>name</c>/<c>property</c> values considered identifying metadata.</summary>
    private static readonly HashSet<string> MetaNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "author", "generator", "copyright", "description", "keywords",
        "dcterms.creator", "dcterms.author", "dc.creator", "dc.publisher",
        "dc.rights", "dc.title", "dc.description", "article:author",
        "twitter:creator", "application-name", "revised", "owner",
    };

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

        HtmlDocument doc = Load(inputPath);
        RemoveNodes(doc, removed, options);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        doc.Save(finalOut);
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, new FileInfo(finalOut).Length, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        HtmlDocument doc = Load(inputPath);
        var found = new List<MetadataEntry>();
        RemoveNodes(doc, found, new MetadataCleanOptions(), inspectOnly: true);
        return found;
    }

    private static HtmlDocument Load(string path)
    {
        var doc = new HtmlDocument { OptionWriteEmptyNodes = true };
        try
        {
            doc.Load(path);
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to parse HTML: {path}", ex) { FilePath = path };
        }

        return doc;
    }

    private static void RemoveNodes(HtmlDocument doc, List<MetadataEntry> removed, MetadataCleanOptions options, bool inspectOnly = false)
    {
        var toRemove = new List<HtmlNode>();

        foreach (HtmlNode node in doc.DocumentNode.Descendants().ToList())
        {
            string name = node.Name.ToLowerInvariant();

            if (name == "meta")
            {
                string key = node.GetAttributeValue("name", null)
                    ?? node.GetAttributeValue("property", null)
                    ?? node.GetAttributeValue("http-equiv", null)
                    ?? string.Empty;
                if (MetaNames.Contains(key))
                {
                    string content = node.GetAttributeValue("content", string.Empty);
                    removed.Add(new MetadataEntry("HTML/meta", key, content));
                    toRemove.Add(node);
                }

                continue;
            }

            // XMP block.
            if (name is "x:xmpmeta" or "xmpmeta" or "xmp")
            {
                removed.Add(new MetadataEntry("XMP", name, "XMP metadata block"));
                toRemove.Add(node);
                continue;
            }

            // RDF / Dublin Core nodes.
            if (name is "rdf:rdf" || name.StartsWith("dc:", StringComparison.Ordinal) || name.StartsWith("dcterms:", StringComparison.Ordinal))
            {
                removed.Add(new MetadataEntry("RDF/DC", name, "RDF/Dublin Core metadata node"));
                toRemove.Add(node);
            }
        }

        if (!inspectOnly)
        {
            foreach (HtmlNode node in toRemove)
            {
                node.Remove();
            }
        }
    }
}
