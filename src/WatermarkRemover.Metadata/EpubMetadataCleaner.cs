using System.Diagnostics;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips the OPF package metadata (Dublin Core elements and custom <c>meta</c> entries)
/// from EPUB files by rewriting the underlying ZIP container. The OPF
/// <c>dc:identifier</c> element is kept — but with a freshly-generated UUID — so the
/// resulting EPUB remains a structurally valid container that ebook readers can
/// still open. Every other entry in the archive (XHTML, CSS, images, fonts, NCX) is
/// preserved byte-for-byte. The canonical <c>mimetype</c> entry stays first and
/// uncompressed, as the OCF / EPUB spec requires.
/// </summary>
public sealed class EpubMetadataCleaner : IFileMetadataCleaner
{
    /// <summary>OPF 3.0 package namespace.</summary>
    internal static readonly XNamespace OpfNs = XNamespace.Get("http://www.idpf.org/2007/opf");

    /// <summary>Dublin Core Terms namespace used by <c>dc:*</c> elements in OPF.</summary>
    internal static readonly XNamespace DcNs = XNamespace.Get("http://purl.org/dc/elements/1.1/");

    /// <summary>OCF container namespace used in <c>META-INF/container.xml</c>.</summary>
    internal static readonly XNamespace ContainerNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:container");

    private static readonly string[] Extensions = [".epub"];

    private const string MimetypeEntryName = "mimetype";
    private const string MimetypeValue = "application/epub+zip";
    private const string ContainerEntryName = "META-INF/container.xml";

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
        byte[] sanitizedOpf;
        string opfEntryName;

        try
        {
            using FileStream fs = File.OpenRead(inputPath);
            using ZipArchive input = new(fs, ZipArchiveMode.Read, leaveOpen: false);

            ValidateMimetypeEntry(input);
            opfEntryName = ResolveOpfEntryName(input);
            sanitizedOpf = BuildSanitizedOpf(input, opfEntryName, options, removed);
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new MetadataStripException($"Corrupt EPUB archive: {inputPath}", ex) { FilePath = inputPath };
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to process EPUB: {inputPath}", ex) { FilePath = inputPath };
        }

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        try
        {
            WriteRewrittenArchive(inputPath, finalOut, opfEntryName, sanitizedOpf);
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to write EPUB: {finalOut}", ex) { FilePath = finalOut };
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
            using FileStream fs = File.OpenRead(inputPath);
            using ZipArchive input = new(fs, ZipArchiveMode.Read, leaveOpen: false);

            ValidateMimetypeEntry(input);
            string opfEntryName = ResolveOpfEntryName(input);
            CollectMetadataEntries(input, opfEntryName, found);
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new MetadataStripException($"Corrupt EPUB archive: {inputPath}", ex) { FilePath = inputPath };
        }
        catch (Exception ex)
        {
            throw new MetadataStripException($"Failed to inspect EPUB: {inputPath}", ex) { FilePath = inputPath };
        }

        return found;
    }

    private static void ValidateMimetypeEntry(ZipArchive archive)
    {
        ZipArchiveEntry? mimetype = archive.GetEntry(MimetypeEntryName);
        if (mimetype is null)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: missing required '{MimetypeEntryName}' entry at archive root.");
        }

        using Stream stream = mimetype.Open();
        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        if (!string.Equals(content, MimetypeValue, StringComparison.Ordinal))
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: '{MimetypeEntryName}' entry must contain '{MimetypeValue}'.");
        }
    }

    private static string ResolveOpfEntryName(ZipArchive archive)
    {
        ZipArchiveEntry? container = archive.GetEntry(ContainerEntryName);
        if (container is null)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: missing required '{ContainerEntryName}'.");
        }

        XDocument doc;
        try
        {
            using Stream stream = container.Open();
            doc = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: '{ContainerEntryName}' is not well-formed XML.", ex);
        }

        XElement? rootfile = doc
            .Descendants(ContainerNs + "rootfile")
            .FirstOrDefault();
        if (rootfile is null)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: '{ContainerEntryName}' has no <rootfile> child.");
        }

        string? fullPath = (string?)rootfile.Attribute("full-path");
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: <rootfile> in '{ContainerEntryName}' has no full-path attribute.");
        }

        ZipArchiveEntry? opf = archive.GetEntry(fullPath);
        if (opf is null)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: container points to '{fullPath}' which is not in the archive.");
        }

        return fullPath;
    }

    private static void CollectMetadataEntries(ZipArchive archive, string opfEntryName, List<MetadataEntry> output)
    {
        XDocument opf = ReadOpf(archive, opfEntryName);
        XElement? metadata = opf.Root?.Element(OpfNs + "metadata");
        if (metadata is null)
        {
            return;
        }

        foreach (XElement element in metadata.Elements().ToList())
        {
            string name = element.Name.LocalName;
            if (element.Name.Namespace == DcNs)
            {
                if (!string.IsNullOrWhiteSpace(element.Value))
                {
                    output.Add(new MetadataEntry("OPF/dc", name, element.Value.Trim()));
                }
            }
            else if (element.Name.Namespace == OpfNs && name == "meta")
            {
                string key = (string?)element.Attribute("property")
                    ?? (string?)element.Attribute("name")
                    ?? "meta";
                string value = (string?)element.Attribute("content")
                    ?? element.Value
                    ?? string.Empty;
                output.Add(new MetadataEntry("OPF/meta", key, value));
            }
        }
    }

    private static byte[] BuildSanitizedOpf(
        ZipArchive archive,
        string opfEntryName,
        MetadataCleanOptions options,
        List<MetadataEntry> removed)
    {
        XDocument opf = ReadOpf(archive, opfEntryName);
        XElement? metadata = opf.Root?.Element(OpfNs + "metadata");
        if (metadata is null)
        {
            // No metadata block to strip; return the original payload byte-for-byte.
            ZipArchiveEntry opfEntry = archive.GetEntry(opfEntryName)
                ?? throw new MetadataStripException("OPF entry disappeared mid-processing.");
            using var ms = new MemoryStream();
            using (Stream s = opfEntry.Open())
            {
                s.CopyTo(ms);
            }

            return ms.ToArray();
        }

        foreach (XElement element in metadata.Elements().ToList())
        {
            string name = element.Name.LocalName;

            if (element.Name.Namespace == DcNs)
            {
                // dc:identifier is required by the OPF spec — keep it, but with a fresh UUID.
                if (string.Equals(name, "identifier", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(element.Value))
                    {
                        removed.Add(new MetadataEntry("OPF/dc", name, element.Value.Trim()));
                    }

                    element.Value = $"urn:uuid:{Guid.NewGuid():D}";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(element.Value))
                {
                    removed.Add(new MetadataEntry("OPF/dc", name, element.Value.Trim()));
                }

                element.Remove();
            }
            else if (element.Name.Namespace == OpfNs && name == "meta")
            {
                string key = (string?)element.Attribute("property")
                    ?? (string?)element.Attribute("name")
                    ?? "meta";
                string value = (string?)element.Attribute("content")
                    ?? element.Value
                    ?? string.Empty;
                removed.Add(new MetadataEntry("OPF/meta", key, value));
                element.Remove();
            }
        }

        // Ensure the metadata block has at least one identifier — the OPF spec
        // requires a primary identifier, and `epubcheck` rejects documents without one.
        if (metadata.Element(DcNs + "identifier") is null)
        {
            metadata.AddFirst(new XElement(DcNs + "identifier", $"urn:uuid:{Guid.NewGuid():D}"));
        }

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Indent = false,
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, settings))
        {
            opf.Save(writer);
        }

        return output.ToArray();
    }

    private static XDocument ReadOpf(ZipArchive archive, string opfEntryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(opfEntryName)
            ?? throw new MetadataStripException(
                $"Not a valid EPUB file: OPF entry '{opfEntryName}' not found.");

        try
        {
            using Stream stream = entry.Open();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new MetadataStripException(
                $"Not a valid EPUB file: OPF '{opfEntryName}' is not well-formed XML.", ex);
        }
    }

    private static void WriteRewrittenArchive(
        string inputPath,
        string outputPath,
        string opfEntryName,
        byte[] sanitizedOpf)
    {
        // Write to a sibling temp file first so an in-place write (inputPath == outputPath)
        // doesn't truncate the source we are still reading from.
        string tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream inputFs = File.OpenRead(inputPath))
            using (ZipArchive input = new(inputFs, ZipArchiveMode.Read, leaveOpen: false))
            using (FileStream outputFs = File.Create(tempPath))
            using (ZipArchive output = new(outputFs, ZipArchiveMode.Create, leaveOpen: false))
            {
                bool mimetypeWritten = false;
                foreach (ZipArchiveEntry entry in input.Entries)
                {
                    if (string.Equals(entry.FullName, MimetypeEntryName, StringComparison.Ordinal))
                    {
                        // Always first, always uncompressed, content rewritten verbatim.
                        WriteEntry(output, entry.FullName, OpenEntryBytes(entry), CompressionLevel.NoCompression);
                        mimetypeWritten = true;
                        continue;
                    }

                    if (string.Equals(entry.FullName, opfEntryName, StringComparison.Ordinal))
                    {
                        WriteEntry(output, entry.FullName, sanitizedOpf, CompressionLevel.Optimal);
                        continue;
                    }

                    // Preserve every other entry byte-for-byte.
                    CompressionLevel level = entry.FullName == MimetypeEntryName
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.Optimal;
                    WriteEntry(output, entry.FullName, OpenEntryBytes(entry), level);
                }

                if (!mimetypeWritten)
                {
                    throw new MetadataStripException(
                        $"Not a valid EPUB file: source archive has no '{MimetypeEntryName}' entry.");
                }
            }

            // Replace the destination atomically.
            if (File.Exists(outputPath))
            {
                File.Replace(tempPath, outputPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, outputPath);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp file.
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // ignore
            }

            throw;
        }
    }

    private static byte[] OpenEntryBytes(ZipArchiveEntry entry)
    {
        using var ms = new MemoryStream();
        using Stream s = entry.Open();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] data, CompressionLevel compression)
    {
        ZipArchiveEntry created = archive.CreateEntry(name, compression);
        using Stream s = created.Open();
        s.Write(data, 0, data.Length);
    }
}
