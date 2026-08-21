using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips EXIF, XMP, IPTC (text chunks) and C2PA (<c>caBX</c>) chunks from PNG files at the
/// chunk level, preserving the image data (IDAT) exactly.
/// </summary>
public sealed class PngMetadataCleaner : IFileMetadataCleaner
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly string[] Extensions = [".png"];

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) => Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sw = Stopwatch.StartNew();
        byte[] input = ReadFile(inputPath);
        var removed = new List<MetadataEntry>();
        byte[] output = Process(input, options, removed, inspectOnly: false);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        File.WriteAllBytes(finalOut, output);
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, input.LongLength, output.LongLength, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        byte[] input = ReadFile(inputPath);
        var found = new List<MetadataEntry>();
        Process(input, new MetadataCleanOptions(), found, inspectOnly: true);
        return found;
    }

    private static byte[] ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new MetadataStripException($"File not found: {path}") { FilePath = path };
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new MetadataStripException($"Not a valid PNG file: {path}") { FilePath = path };
        }

        return bytes;
    }

    private static byte[] Process(byte[] data, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(Signature, 0, 8);
        int pos = 8;

        try
        {
            while (pos + 8 <= data.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos, 4));
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                int chunkTotal = 12 + length; // length(4) + type(4) + data + crc(4)
                if (length < 0 || pos + chunkTotal > data.Length)
                {
                    throw new MetadataStripException("Corrupt PNG chunk length encountered.");
                }

                bool remove = ShouldRemove(type, options, out string container, out string key, out string value);
                if (remove)
                {
                    removed.Add(new MetadataEntry(container, key, value));
                }
                else if (!inspectOnly)
                {
                    output.Write(data, pos, chunkTotal);
                }

                pos += chunkTotal;

                if (type == "IEND")
                {
                    break;
                }
            }
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new MetadataStripException("Corrupt PNG structure encountered while stripping metadata.", ex);
        }

        return output.ToArray();
    }

    private static bool ShouldRemove(string type, MetadataCleanOptions options, out string container, out string key, out string value)
    {
        container = "PNG";
        key = type;
        value = string.Empty;

        switch (type)
        {
            case "eXIf":
                container = "EXIF"; value = "EXIF chunk"; return options.StripExif;
            case "tEXt":
            case "zTXt":
            case "iTXt":
                container = "XMP/Text"; value = $"{type} textual metadata"; return options.StripXmp || options.StripIptc;
            case "tIME":
                container = "PNG"; value = "Last-modification time"; return options.StripExif;
            case "caBX": // C2PA JUMBF box chunk
                container = "C2PA"; value = "C2PA manifest chunk"; return options.StripC2pa;
            case "iCCP":
                container = "ICC"; value = "ICC color profile"; return !options.PreserveColorProfile;
            default:
                return false;
        }
    }
}
