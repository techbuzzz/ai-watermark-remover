using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips EXIF, XMP and ICC color-profile boxes from AVIF files at the ISO base
/// media file format (ISOBMFF / ISO 14496-12) box level, preserving the image
/// bitstream (<c>mdat</c>) and the structural metadata boxes (<c>hdlr</c>,
/// <c>pitm</c>, <c>iloc</c>, <code>iinf</code>, <c>iprp</c>, …) bit-for-bit.
/// </summary>
/// <remarks>
/// <para>
/// AVIF reuses the same ISOBMFF container as HEIF; the file-level difference is
/// only the <c>ftyp</c> brand. Structure:
/// <code>
/// box   = size (BE u32) | type (4CC) | payload
/// FullBox = box header | version (1) | flags (3) | payload
/// file  = ftyp | meta (FullBox) | mdat | …
/// meta  = hdlr | pitm | iloc | iinf | iprp | Exif | uuid(EXIF/XMP) | colr(ICC) | mime(XMP) | …
/// </code>
/// </para>
/// <para>
/// The walker is recursive: <c>meta</c> is the only box whose children are
/// inspected for strippable children, and only boxes that match the
/// metadata-bearing fourcc / UUID patterns are removed — every other
/// structural box (<c>hdlr</c>, <c>iloc</c>, <code>iinf</code>, <c>iprp</c>,
/// <c>ipco</c>, <c>ipma</c>, …) is preserved bit-for-bit. <c>largesize</c>
/// boxes (size == 1 followed by an 8-byte BE u64) are honoured at the top
/// level so very large <c>mdat</c> regions are not miscounted.
/// </para>
/// <para>
/// EXIF is stored either as a 4CC <c>Exif</c> box (ISO 23008-12 § A.2) or as a
/// <c>uuid</c> box carrying the Apple-defined UUID
/// <c>8532C9A2-3B9A-11E4-B6A2-0401E0CBBFCE</c>. XMP is carried in a <c>uuid</c>
/// box with UUID <c>BE7ACFCB-97A9-42E8-9C71-999491E3AFAC</c> or, less commonly,
/// in a <c>mime</c> box whose content type is <c>application/rdf+xml</c>. ICC
/// color profiles live in a <c>colr</c> box with a <c>rICC</c> or <c>prof</c>
/// colour-type code; the <c>nclx</c> colour-type is the inline colour
/// primaries / transfer / matrix representation and is kept verbatim because
/// it is the image's actual colour description, not metadata.
/// </para>
/// </remarks>
public sealed class AvifMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".avif"];

    /// <summary>AVIF brands accepted in the <c>ftyp</c> compatible-brands list.</summary>
    /// <remarks>
    /// The base HEIF brand <c>mif1</c> is also accepted because AVIF images
    /// emitted by libavif and other conformant encoders always carry
    /// <c>mif1</c> in the compatible-brands list per ISO 23000-22.
    /// </remarks>
    private static readonly HashSet<string> AvifBrands = new(StringComparer.Ordinal)
    {
        "avif", "avis", "mif1",
    };

    /// <summary>UUID the spec uses for EXIF data inside AVIF <c>meta</c>.</summary>
    private static readonly byte[] AppleExifUuid =
    [
        0x85, 0x32, 0xC9, 0xA2, 0x3B, 0x9A, 0x11, 0xE4,
        0xB6, 0xA2, 0x04, 0x01, 0xE0, 0xCB, 0xBF, 0xCE,
    ];

    /// <summary>UUID used for XMP data inside AVIF <c>meta</c>.</summary>
    private static readonly byte[] XmpUuid =
    [
        0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8,
        0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC,
    ];

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) => Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sw = Stopwatch.StartNew();
        byte[] input = ReadFile(inputPath);
        long inputSize = input.LongLength;

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
        if (!IsValidAvif(bytes))
        {
            throw new MetadataStripException($"Not a valid AVIF file: {path}") { FilePath = path };
        }

        return bytes;
    }

    /// <summary>
    /// Validates the ISOBMFF header: a recognisable <c>ftyp</c> box at offset 0
    /// that lists at least one AVIF brand (major_brand or any compatible_brand).
    /// </summary>
    private static bool IsValidAvif(byte[] bytes)
    {
        if (bytes.Length < 16)
        {
            return false;
        }

        // ftyp box header: size (BE u32) + "ftyp" (4 bytes).
        uint ftypSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));
        if (ftypSize is 0 or < 16)
        {
            return false;
        }

        if (bytes[4] != (byte)'f' || bytes[5] != (byte)'t' || bytes[6] != (byte)'y' || bytes[7] != (byte)'p')
        {
            return false;
        }

        if (ftypSize > bytes.Length)
        {
            return false;
        }

        // major_brand occupies bytes 8..11, minor_version 12..15.
        string majorBrand = Encoding.ASCII.GetString(bytes, 8, 4);
        if (AvifBrands.Contains(majorBrand))
        {
            return true;
        }

        // Walk the compatible_brands list to see if any brand matches.
        int compatibleStart = 16;
        int compatibleEnd = (int)Math.Min(ftypSize, bytes.Length);
        for (int p = compatibleStart; p + 4 <= compatibleEnd; p += 4)
        {
            string brand = Encoding.ASCII.GetString(bytes, p, 4);
            if (AvifBrands.Contains(brand))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Top-level walker. Each top-level box is parsed (size + type); the <c>meta</c>
    /// box is rebuilt by walking its children, every other box is copied verbatim.
    /// </summary>
    private static byte[] Process(byte[] data, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        using var output = new MemoryStream(data.Length);

        int pos = 0;
        try
        {
            while (pos < data.Length)
            {
                BoxHeader header = ReadBoxHeader(data, pos);
                string type = header.Type;
                int boxEnd = header.PayloadStart + header.PayloadSize;

                if (type == "meta")
                {
                    RebuildMetaBox(data, header, options, removed, inspectOnly, output);
                }
                else if (!inspectOnly)
                {
                    // ftyp / mdat / free / skip / unknown — copy the whole box verbatim.
                    output.Write(data, pos, header.TotalSize);
                }

                pos = boxEnd;
            }
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            throw new MetadataStripException("Corrupt AVIF structure encountered while stripping metadata.", ex);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Rebuilds the <c>meta</c> FullBox: writes the 12-byte header
    /// (size + "meta" + version + flags) verbatim, then walks the children
    /// and copies every box that does not match a metadata-strip pattern.
    /// The rebuilt header size is patched at the end.
    /// </summary>
    private static void RebuildMetaBox(
        byte[] data,
        BoxHeader metaHeader,
        MetadataCleanOptions options,
        List<MetadataEntry> removed,
        bool inspectOnly,
        MemoryStream output)
    {
        // meta is a FullBox: 8 bytes standard header + 4 bytes version+flags
        // (the payload starts at offset 12 from the start of the box).
        const int fullBoxOverhead = 12;

        if (metaHeader.PayloadSize < 4)
        {
            throw new MetadataStripException("Truncated `meta` FullBox (missing version+flags).");
        }

        int childrenStart = metaHeader.PayloadStart + 4; // skip version + flags
        int childrenEnd = metaHeader.PayloadStart + metaHeader.PayloadSize;

        if (!inspectOnly)
        {
            // Reserve the 12-byte meta header — patch the size at the end.
            long headerPos = output.Position;
            output.Write(data, metaHeader.Start, fullBoxOverhead);

            int childPos = childrenStart;
            while (childPos < childrenEnd)
            {
                BoxHeader child = ReadBoxHeader(data, childPos);
                if (ShouldRemoveChild(data, child, options, out string container, out string key, out string value))
                {
                    removed.Add(new MetadataEntry(container, key, value));
                }
                else
                {
                    output.Write(data, childPos, child.TotalSize);
                }

                childPos += child.TotalSize;
            }

            // Patch the meta box size: (current length) - (headerPos) - 8 standard
            // header bytes that the size field excludes, then convert to absolute
            // u32 from the start of the box header.
            long metaEnd = output.Position;
            long metaSize = metaEnd - headerPos;
            if (metaSize > uint.MaxValue)
            {
                throw new MetadataStripException("Rebuilt `meta` box exceeds 4 GiB limit.");
            }

            Span<byte> sizeBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, (uint)metaSize);
            output.Position = headerPos;
            output.Write(sizeBytes);
            output.Position = metaEnd;
        }
        else
        {
            int childPos = childrenStart;
            while (childPos < childrenEnd)
            {
                BoxHeader child = ReadBoxHeader(data, childPos);
                if (ShouldRemoveChild(data, child, options, out string container, out string key, out string value))
                {
                    removed.Add(new MetadataEntry(container, key, value));
                }

                childPos += child.TotalSize;
            }
        }
    }

    /// <summary>
    /// Decides whether a child of <c>meta</c> should be stripped. Returns false
    /// (keep) for every structural box (<c>hdlr</c>, <c>pitm</c>, <c>iloc</c>,
    /// <code>iinf</code>, <c>iprp</c>, …) and only true for the four well-known
    /// metadata carriers: 4CC <c>Exif</c>, EXIF/XMP <c>uuid</c> boxes, ICC
    /// <c>colr</c> boxes with a <c>rICC</c> / <c>prof</c> colour type, and
    /// <c>mime</c> boxes whose content type is <c>application/rdf+xml</c>.
    /// </summary>
    private static bool ShouldRemoveChild(
        byte[] data,
        BoxHeader child,
        MetadataCleanOptions options,
        out string container,
        out string key,
        out string value)
    {
        container = "AVIF";
        key = child.Type;
        value = $"{child.Type} box";

        switch (child.Type)
        {
            case "Exif":
                container = "EXIF";
                key = "Exif";
                value = "EXIF box (ISO 23008-12 § A.2)";
                return options.StripExif;

            case "uuid":
            {
                if (child.PayloadSize < 16)
                {
                    return false;
                }

                var uuid = new ReadOnlySpan<byte>(data, child.PayloadStart, 16);
                if (uuid.SequenceEqual(AppleExifUuid))
                {
                    container = "EXIF";
                    key = "uuid/EXIF";
                    value = "EXIF data in Apple UUID box";
                    return options.StripExif;
                }

                if (uuid.SequenceEqual(XmpUuid))
                {
                    container = "XMP";
                    key = "uuid/XMP";
                    value = "XMP packet in UUID box";
                    return options.StripXmp;
                }

                // Some other uuid box (e.g. a private one) — keep it.
                container = "AVIF";
                key = "uuid/other";
                value = "UUID box (non-EXIF, non-XMP)";
                return false;
            }

            case "colr":
            {
                // The colour_type 4CC occupies bytes 8..11 of the box (right
                // after the standard box header). `nclx` is the inline colour
                // primaries / transfer / matrix representation and must be
                // kept; `rICC` and `prof` reference an embedded ICC profile
                // and are stripped unless PreserveColorProfile is set.
                if (child.PayloadSize < 4)
                {
                    return false;
                }

                string colourType = Encoding.ASCII.GetString(data, child.PayloadStart, 4);
                if (colourType is "rICC" or "prof")
                {
                    container = "ICC";
                    key = "colr/ICC";
                    value = $"ICC color profile ({colourType})";
                    return !options.PreserveColorProfile;
                }

                // `nclx` and any other colour_type — keep (it is structural colour info).
                return false;
            }

            case "mime":
            {
                // mime box payload starts with a null-terminated ASCII content_type.
                int payloadLen = Math.Min(child.PayloadSize, 64);
                int nulAt = -1;
                for (int i = 0; i < payloadLen; i++)
                {
                    if (data[child.PayloadStart + i] == 0)
                    {
                        nulAt = i;
                        break;
                    }
                }

                string contentType = nulAt < 0
                    ? Encoding.ASCII.GetString(data, child.PayloadStart, payloadLen)
                    : Encoding.ASCII.GetString(data, child.PayloadStart, nulAt);

                if (contentType.Equals("application/rdf+xml", StringComparison.OrdinalIgnoreCase))
                {
                    container = "XMP";
                    key = "mime/XMP";
                    value = "XMP packet in mime box";
                    return options.StripXmp;
                }

                return false;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Resolves the size and 4CC type of the box starting at <paramref name="pos"/>,
    /// honouring the 8-byte <c>largesize</c> extension (size field == 1).
    /// </summary>
    private static BoxHeader ReadBoxHeader(byte[] data, int pos)
    {
        if (pos + 8 > data.Length)
        {
            throw new MetadataStripException("Truncated AVIF box header.");
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
        string type = Encoding.ASCII.GetString(data, pos + 4, 4);

        int headerSize;
        long boxSize;
        if (size32 == 1)
        {
            // largesize: 8-byte BE u64 follows the type 4CC.
            if (pos + 16 > data.Length)
            {
                throw new MetadataStripException("Truncated AVIF largesize header.");
            }

            boxSize = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(pos + 8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            // size == 0 means "extends to EOF". This is rare for top-level boxes
            // and complicates the walker; refuse rather than miscount.
            throw new MetadataStripException("AVIF box with size==0 is not supported.");
        }
        else
        {
            boxSize = size32;
            headerSize = 8;
        }

        if (boxSize < headerSize)
        {
            throw new MetadataStripException("AVIF box size is smaller than its header.");
        }

        if (pos + boxSize > data.Length)
        {
            throw new MetadataStripException("Truncated AVIF box payload.");
        }

        return new BoxHeader(
            Start: pos,
            TotalSize: (int)boxSize,
            HeaderSize: headerSize,
            PayloadStart: pos + headerSize,
            PayloadSize: (int)(boxSize - headerSize),
            Type: type);
    }

    /// <summary>Decoded view of an ISOBMFF box header.</summary>
    private readonly record struct BoxHeader(
        int Start,
        int TotalSize,
        int HeaderSize,
        int PayloadStart,
        int PayloadSize,
        string Type);
}
