using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips EXIF, XMP, IPTC, MakerNotes and C2PA (JUMBF APP11) from JPEG files at the byte level,
/// leaving the compressed image scan data untouched (pixels are bit-for-bit unchanged).
/// </summary>
public sealed class JpegMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".jpe", ".jfif"];

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) =>
        Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sw = Stopwatch.StartNew();
        byte[] input = ReadFile(inputPath);
        long inputSize = input.Length;

        var removed = new List<MetadataEntry>();
        byte[] output = Process(input, options, removed, inspectOnly: false);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        File.WriteAllBytes(finalOut, output);
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, output.LongLength, sw.Elapsed);
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
        if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            throw new MetadataStripException($"Not a valid JPEG file: {path}") { FilePath = path };
        }

        return bytes;
    }

    private static byte[] Process(byte[] data, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        using var output = new MemoryStream(data.Length);
        int pos = 0;

        // SOI
        output.Write(data, 0, 2);
        pos = 2;

        try
        {
            while (pos + 1 < data.Length)
            {
                if (data[pos] != 0xFF)
                {
                    // Unexpected: copy remainder and bail.
                    output.Write(data, pos, data.Length - pos);
                    pos = data.Length;
                    break;
                }

                byte marker = data[pos + 1];

                // Standalone markers (no length): RSTn, SOI, EOI, TEM.
                if (marker is 0xD8 or 0xD9 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    if (!inspectOnly)
                    {
                        output.Write(data, pos, 2);
                    }

                    pos += 2;
                    continue;
                }

                if (pos + 3 >= data.Length)
                {
                    output.Write(data, pos, data.Length - pos);
                    pos = data.Length;
                    break;
                }

                int segLen = (data[pos + 2] << 8) | data[pos + 3];
                int segStart = pos;
                int segTotal = 2 + segLen;
                if (segStart + segTotal > data.Length)
                {
                    segTotal = data.Length - segStart;
                }

                bool remove = ShouldRemove(marker, data, segStart, segLen, options, out string container, out string key, out string value);
                if (remove)
                {
                    removed.Add(new MetadataEntry(container, key, value));
                    if (inspectOnly)
                    {
                        // continue scanning
                    }
                }
                else if (!inspectOnly)
                {
                    output.Write(data, segStart, segTotal);
                }

                pos = segStart + segTotal;

                // Start of Scan: entropy-coded data follows to EOI; copy verbatim.
                if (marker == 0xDA)
                {
                    if (!inspectOnly)
                    {
                        output.Write(data, pos, data.Length - pos);
                    }

                    pos = data.Length;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new MetadataStripException("Corrupt JPEG structure encountered while stripping metadata.", ex);
        }

        return output.ToArray();
    }

    private static bool ShouldRemove(byte marker, byte[] data, int segStart, int segLen, MetadataCleanOptions options, out string container, out string key, out string value)
    {
        container = "JPEG";
        key = $"marker-FF{marker:X2}";
        value = string.Empty;

        int payloadStart = segStart + 4;
        int payloadLen = Math.Max(0, Math.Min(segLen - 2, data.Length - payloadStart));
        string signature = payloadLen > 0 ? ReadSignature(data, payloadStart, payloadLen) : string.Empty;

        switch (marker)
        {
            case 0xE1: // APP1: EXIF or XMP
                if (signature.StartsWith("Exif", StringComparison.Ordinal))
                {
                    container = "EXIF"; key = "APP1/Exif"; value = "EXIF metadata block";
                    return options.StripExif;
                }

                if (signature.StartsWith("http://ns.adobe.com/xap", StringComparison.Ordinal) || signature.Contains("xmpmeta", StringComparison.Ordinal))
                {
                    container = "XMP"; key = "APP1/XMP"; value = "XMP packet";
                    return options.StripXmp;
                }

                container = "APP1"; key = "APP1"; value = signature;
                return options.StripExif || options.StripXmp;

            case 0xED: // APP13: IPTC / Photoshop
                container = "IPTC"; key = "APP13/Photoshop"; value = "IPTC/Photoshop resource block";
                return options.StripIptc;

            case 0xEB: // APP11: JUMBF / C2PA
                container = "C2PA"; key = "APP11/JUMBF"; value = "C2PA JUMBF manifest box";
                return options.StripC2pa;

            case 0xE2: // APP2: ICC profile or FlashPix
                if (signature.StartsWith("ICC_PROFILE", StringComparison.Ordinal))
                {
                    container = "ICC"; key = "APP2/ICC_PROFILE"; value = "ICC color profile";
                    return !options.PreserveColorProfile;
                }

                container = "APP2"; key = "APP2"; value = signature;
                return options.StripMakerNotes;

            case 0xFE: // COM comment
                container = "COM"; key = "Comment"; value = ReadAscii(data, payloadStart, payloadLen);
                return true;

            // Other application markers frequently carry maker notes / vendor metadata.
            case >= 0xE3 and <= 0xEF:
                container = "APPn"; key = $"APP{marker - 0xE0}"; value = signature;
                return options.StripMakerNotes;

            default:
                return false;
        }
    }

    private static string ReadSignature(byte[] data, int start, int len)
    {
        int n = Math.Min(len, 32);
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
        {
            byte b = data[start + i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '\0');
        }

        return sb.ToString().Replace("\0", string.Empty);
    }

    private static string ReadAscii(byte[] data, int start, int len)
    {
        int n = Math.Min(len, 120);
        return Encoding.ASCII.GetString(data, start, n).Trim();
    }
}
