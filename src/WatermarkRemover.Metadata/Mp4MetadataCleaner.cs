using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips authorship, GPS, and provenance metadata from MP4 / MOV / M4V /
/// M4A / 3GP files at the ISO base media file format (ISOBMFF /
/// ISO 14496-12) box level, preserving the audio / video bitstreams
/// (<c>mdat</c>) and the structural boxes that drive playback
/// (<c>mvhd</c>, <c>trak</c>, <c>edts</c>, <c>mvex</c>, <c>stbl</c>, …)
/// bit-for-bit.
/// </summary>
/// <remarks>
/// <para>
/// MP4 / MOV / 3GP / QuickTime all share the same ISOBMFF wire format as
/// HEIF / AVIF; the file-level difference is the <c>ftyp</c> brand. The
/// relevant metadata-bearing boxes for AI-generated media are:
/// <code>
/// file = ftyp | moov | mdat | free | skip | meta (opt) | udta (opt) | uuid(opt) | …
/// moov = mvhd | trak | edts | mvex | udta | meta | …
/// udta = ©xyz (GPS) | ©day | ©mak | ©mod | ©swr | ©swc | ©cmt | ©des | ©lyr | ©nam | ©ART | ©alb | ©gen | ©too | …
/// meta = hdlr | keys | ilst | Exif | uuid(EXIF/XMP) | mime(XMP) | colr(ICC) | …
/// </code>
/// </para>
/// <para>
/// Stripping strategy (per the spec from BACKLOG WR-P108):
/// <list type="bullet">
///   <item>Top-level <c>udta</c> and any <c>moov.udta</c> are removed
///         wholesale — every child fourcc is a "user data" payload and
///         none of them is required for playback. The <c>©xyz</c> atom
///         is the GPS coordinate set; <c>©mak</c> / <c>©mod</c> /
///         <c>©swr</c> are device make / model / software;
///         <c>©day</c> / <c>©nam</c> / <c>©ART</c> / <c>©cmt</c> are
///         creation date / title / artist / comment. All go.</item>
///   <item>Inside any <c>meta</c> (top-level or <c>moov.meta</c>), the
///         <c>keys</c> key-namespace index and the <c>ilst</c> data
///         list are removed — these are the QuickTime title /
///         author / album / genre payload. EXIF (<c>Exif</c> + the
///         Apple UUID), XMP (the XMP UUID + the <c>mime</c> box with
///         <c>application/rdf+xml</c>), and ICC color profiles
///         (<c>colr</c> with <c>rICC</c> / <c>prof</c> colour type)
///         are removed using the same policy as the AVIF cleaner.</item>
///   <item>Every other structural box is copied bit-for-bit. The
///         <c>mdat</c> bitstream (which may be gigabytes for a long
///         video) is never copied through a managed buffer; it is
///         streamed directly from the input to the output via
///         <see cref="Stream.CopyTo(Stream)"/>.</item>
/// </list>
/// </para>
/// <para>
/// <c>largesize</c> boxes (size == 1 followed by an 8-byte BE u64) are
/// honoured for <c>mdat</c> so very large <c>mdat</c> regions are not
/// miscounted.
/// </para>
/// </remarks>
public sealed class Mp4MetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".mp4", ".mov", ".m4v", ".m4a", ".m4b", ".m4p", ".3gp", ".3g2"];

    /// <summary>MP4 / MOV / 3GP / iTunes brands accepted in the
    /// <c>ftyp</c> compatible-brands list.</summary>
    private static readonly HashSet<string> Brands = new(StringComparer.Ordinal)
    {
        "isom", "mp41", "mp42", "mp71",
        "qt  ", // QuickTime (two trailing spaces)
        "M4V ", "M4A ", "M4B ", "M4P ",
        "3gp4", "3gp5", "3gp6",
        "3g2a",
        "avc1", // MP4 with H.264 in the brand list
    };

    /// <summary>UUID the spec uses for EXIF data inside QuickTime <c>meta</c>.</summary>
    private static readonly byte[] AppleExifUuid =
    [
        0x85, 0x32, 0xC9, 0xA2, 0x3B, 0x9A, 0x11, 0xE4,
        0xB6, 0xA2, 0x04, 0x01, 0xE0, 0xCB, 0xBF, 0xCE,
    ];

    /// <summary>UUID used for XMP data inside QuickTime <c>meta</c>.</summary>
    private static readonly byte[] XmpUuid =
    [
        0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8,
        0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC,
    ];

    /// <summary>Latin-1 byte 0xA9 used as the high byte of every <c>©xxx</c>
    /// user-data fourcc (©xyz, ©day, ©mak, …). ISO 14496-12 § 4.3 defines
    /// the 4CC encoding as 4 raw bytes in Mac OS Roman / Latin-1; the
    /// copyright-sign character is therefore 0xA9, not the two-byte
    /// UTF-8 sequence 0xC2 0xA9.</summary>
    private const byte CopyrightSignByte = 0xA9;

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) =>
        Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sw = Stopwatch.StartNew();

        long inputSize = new FileInfo(inputPath).Length;

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        var removed = new List<MetadataEntry>();
        Process(inputPath, finalOut, options, removed);

        long outputSize = new FileInfo(finalOut).Length;
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, outputSize, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new MetadataStripException($"File not found: {inputPath}") { FilePath = inputPath };
        }

        byte[] bytes = File.ReadAllBytes(inputPath);
        if (!IsValidMp4(bytes))
        {
            throw new MetadataStripException($"Not a valid MP4 / MOV / 3GP file: {inputPath}") { FilePath = inputPath };
        }

        var found = new List<MetadataEntry>();
        WalkForInspect(bytes, found);
        return found;
    }

    /// <summary>
    /// Validates the ISOBMFF header: a recognisable <c>ftyp</c> box at
    /// offset 0 that lists at least one MP4/MOV/3GP brand (major_brand
    /// or any compatible_brand).
    /// </summary>
    private static bool IsValidMp4(byte[] bytes)
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
        if (Brands.Contains(majorBrand))
        {
            return true;
        }

        // Walk the compatible_brands list to see if any brand matches.
        int compatibleStart = 16;
        int compatibleEnd = (int)Math.Min(ftypSize, bytes.Length);
        for (int p = compatibleStart; p + 4 <= compatibleEnd; p += 4)
        {
            string brand = Encoding.ASCII.GetString(bytes, p, 4);
            if (Brands.Contains(brand))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Top-level streaming pass: writes ftyp / mdat / moov-out to a
    /// fresh file, only materialising the structural metadata in
    /// memory. mdat is streamed through with zero copy.
    /// </summary>
    private static void Process(
        string inputPath,
        string outputPath,
        MetadataCleanOptions options,
        List<MetadataEntry> removed)
    {
        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        long pos = 0;
        try
        {
            while (pos < input.Length)
            {
                var header = ReadBoxHeaderFromStream(input, pos);
                switch (header.Type)
                {
                    case "ftyp":
                    case "free":
                    case "skip":
                    case "mdat":
                        // Preserve verbatim. mdat may be gigabytes —
                        // we never materialise it.
                        CopyBox(input, output, pos, header.TotalSize);
                        break;

                    case "moov":
                        // Structural movie box: rebuild its children.
                        RewriteMoovBox(input, output, pos, header, options, removed);
                        break;

                    case "meta":
                        // Top-level FullBox meta (HEIF-style): rebuild its children.
                        RewriteFullBoxMeta(input, output, pos, header, options, removed);
                        break;

                    case "udta":
                        // Top-level user data — strip wholesale.
                        removed.Add(new MetadataEntry("USER_DATA", "udta", "user-data box (root level)"));
                        break;

                    case "uuid":
                        // 16-byte UUID-based metadata carrier. EXIF or XMP get stripped;
                        // anything else (e.g. Microsoft Camera bits) is preserved.
                        HandleUuidAtTopLevel(input, output, pos, header, options, removed);
                        break;

                    default:
                        // Unknown top-level box — preserve verbatim.
                        CopyBox(input, output, pos, header.TotalSize);
                        break;
                }

                pos += header.TotalSize;
            }
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MetadataStripException("I/O error while stripping MP4/MOV metadata.", ex)
            {
                FilePath = inputPath,
            };
        }
    }

    /// <summary>
    /// Reads a single box header from <paramref name="input"/> at offset
    /// <paramref name="pos"/>, honouring the 16-byte <c>largesize</c>
    /// extension. After the call, the stream is left positioned at
    /// <paramref name="pos"/> + headerSize (ready for the payload).
    /// </summary>
    private static IsoBoxReader.BoxHeader ReadBoxHeaderFromStream(FileStream input, long pos)
    {
        // Read the 4-byte size and 4-byte type. If size == 1, we have
        // largesize — read 8 more bytes for the 64-bit size.
        input.Position = pos;
        Span<byte> first8 = stackalloc byte[8];
        int read = input.Read(first8);
        if (read < 8)
        {
            throw new MetadataStripException("Truncated MP4 box header.");
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(first8.Slice(0, 4));
        string type = Encoding.Latin1.GetString(first8.Slice(4, 4));

        int headerSize;
        long boxSize;
        if (size32 == 1)
        {
            Span<byte> next8 = stackalloc byte[8];
            int read2 = input.Read(next8);
            if (read2 < 8)
            {
                throw new MetadataStripException("Truncated MP4 largesize header.");
            }

            boxSize = BinaryPrimitives.ReadInt64BigEndian(next8);
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            throw new MetadataStripException("MP4 box with size==0 is not supported.");
        }
        else
        {
            boxSize = size32;
            headerSize = 8;
        }

        if (boxSize < headerSize)
        {
            throw new MetadataStripException("MP4 box size is smaller than its header.");
        }

        if (pos + boxSize > input.Length)
        {
            throw new MetadataStripException("Truncated MP4 box payload.");
        }

        return new IsoBoxReader.BoxHeader(
            Start: (int)pos,
            TotalSize: (int)boxSize,
            HeaderSize: headerSize,
            PayloadStart: (int)(pos + headerSize),
            PayloadSize: (int)(boxSize - headerSize),
            Type: type);
    }

    /// <summary>Streams one box (header + payload) from input to output.</summary>
    private static void CopyBox(FileStream input, FileStream output, long pos, long boxSize)
    {
        input.Position = pos;
        long remaining = boxSize;
        // 64 KiB buffer: large enough to amortise syscall overhead on
        // small boxes, small enough not to balloon working set.
        Span<byte> buf = stackalloc byte[65536];
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buf.Length, remaining);
            int read = input.Read(buf.Slice(0, toRead));
            if (read <= 0)
            {
                throw new MetadataStripException("Unexpected EOF while copying box payload.");
            }

            output.Write(buf.Slice(0, read));
            remaining -= read;
        }
    }

    /// <summary>
    /// Reads the <c>moov</c> box from the input, rebuilds it, and
    /// writes the rebuilt version to the output. Children of
    /// <c>moov</c> are classified:
    /// <list type="bullet">
    ///   <item><c>mvhd</c> / <c>trak</c> / <c>edts</c> / <c>mvex</c> /
    ///         <c>ipma</c> — preserved verbatim.</item>
    ///   <item><c>udta</c> — stripped wholesale (all fourcc children
    ///         are user data; not required for playback).</item>
    ///   <item><c>meta</c> — rewritten to strip the QuickTime keys /
    ///         ilst / EXIF / XMP / ICC inside it.</item>
    /// </list>
    /// </summary>
    private static void RewriteMoovBox(
        FileStream input,
        FileStream output,
        long pos,
        IsoBoxReader.BoxHeader moov,
        MetadataCleanOptions options,
        List<MetadataEntry> removed)
    {
        // Reserve the 8-byte moov header; patch the size at the end.
        long headerPos = output.Position;
        CopyBox(input, output, pos, moov.HeaderSize);

        // Walk children of moov.
        long childPos = moov.PayloadStart;
        long payloadEnd = pos + moov.TotalSize;
        while (childPos < payloadEnd)
        {
            var child = ReadBoxHeaderFromStream(input, childPos);
            switch (child.Type)
            {
                case "udta":
                {
                    // moov.udta: strip everything. Report the well-known
                    // child fourccs we expect to be inside.
                    ReportUserDataChildren(input, child, removed);
                    break;
                }

                case "meta":
                {
                    // moov.meta (QuickTime-style): rewrite the FullBox.
                    RewriteFullBoxMeta(input, output, childPos, child, options, removed);
                    break;
                }

                default:
                    // mvhd, trak, edts, mvex, ipma, … — copy verbatim.
                    CopyBox(input, output, childPos, child.TotalSize);
                    break;
            }

            childPos += child.TotalSize;
        }

        // Patch the moov header size to the new total.
        long moovEnd = output.Position;
        long newSize = moovEnd - headerPos;
        if (newSize > uint.MaxValue)
        {
            throw new MetadataStripException("Rebuilt `moov` box exceeds 4 GiB limit.");
        }

        Span<byte> sizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, (uint)newSize);
        output.Position = headerPos;
        output.Write(sizeBytes);
        output.Position = moovEnd;
    }

    /// <summary>
    /// Reads a FullBox <c>meta</c> from the input stream, walks its
    /// children, and writes a rebuilt version to the output. Children
    /// are classified by the same policy as the AVIF cleaner's meta
    /// walker: <c>hdlr</c> / <c>pitm</c> / <c>iinf</c> / <c>iloc</c> /
    /// <c>iprp</c> / <c>ipco</c> / <c>ipma</c> are preserved; EXIF /
    /// XMP / ICC / <c>keys</c> / <c>ilst</c> are stripped.
    /// </summary>
    private static void RewriteFullBoxMeta(
        FileStream input,
        FileStream output,
        long pos,
        IsoBoxReader.BoxHeader meta,
        MetadataCleanOptions options,
        List<MetadataEntry> removed)
    {
        const int versionFlagsSize = 4;

        if (meta.TotalSize < meta.HeaderSize + versionFlagsSize)
        {
            throw new MetadataStripException("Truncated `meta` FullBox (missing version+flags).");
        }

        // Read the entire meta box (header + payload) into memory —
        // meta is small (the structural metadata, not the bitstream).
        byte[] metaBytes = new byte[meta.TotalSize];
        input.Position = pos;
        int read = input.Read(metaBytes, 0, meta.TotalSize);
        if (read != meta.TotalSize)
        {
            throw new MetadataStripException("Failed to read `meta` box.");
        }

        // The payload starts after the 8-byte box header and contains
        // 4 bytes of version+flags followed by the children. The
        // children are at offset (headerSize + versionFlagsSize) into
        // the box, which is at offset versionFlagsSize into the payload.
        int payloadOffset = meta.HeaderSize;            // start of payload (= 8 for std box)
        int childrenStart = payloadOffset + versionFlagsSize;
        int childrenEnd = meta.TotalSize;
        int childPos = childrenStart;

        // Reserve the box header + version+flags; patch the size at the end.
        long headerPos = output.Position;
        output.Write(metaBytes, 0, payloadOffset + versionFlagsSize);

        while (childPos < childrenEnd)
        {
            var child = IsoBoxReader.Read(metaBytes, childPos);
            if (ShouldRemoveMetaChild(metaBytes, child, options, out string container, out string key, out string value))
            {
                removed.Add(new MetadataEntry(container, key, value));
            }
            else
            {
                output.Write(metaBytes, childPos, child.TotalSize);
            }

            childPos += child.TotalSize;
        }

        // Patch the meta box size.
        long metaEnd = output.Position;
        long newSize = metaEnd - headerPos;
        if (newSize > uint.MaxValue)
        {
            throw new MetadataStripException("Rebuilt `meta` box exceeds 4 GiB limit.");
        }

        Span<byte> sizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, (uint)newSize);
        output.Position = headerPos;
        output.Write(sizeBytes);
        output.Position = metaEnd;
    }

    /// <summary>
    /// Inspects a <c>udta</c> payload and reports the well-known
    /// authorship / provenance / GPS child fourccs that would be
    /// stripped. The fourcc names are produced from the raw 4 bytes
    /// (0xA9 + three ASCII) and matched against the user-data pattern.
    /// </summary>
    private static void ReportUserDataChildren(
        FileStream input,
        IsoBoxReader.BoxHeader udta,
        List<MetadataEntry> removed)
    {
        int payloadSize = udta.PayloadSize;
        if (payloadSize <= 0)
        {
            removed.Add(new MetadataEntry("USER_DATA", "udta", "user-data box"));
            return;
        }

        byte[] payload = new byte[payloadSize];
        input.Position = udta.PayloadStart;
        int read = input.Read(payload, 0, payloadSize);
        if (read != payloadSize)
        {
            throw new MetadataStripException("Failed to read `udta` payload.");
        }

        int pos = 0;
        int entries = 0;
        while (pos + 8 <= payload.Length)
        {
            var child = IsoBoxReader.Read(payload, pos);
            string displayType = DescribeFourcc(child.Type);
            removed.Add(new MetadataEntry("USER_DATA", displayType, "user-data child of `udta`"));
            entries++;
            pos += child.TotalSize;
        }

        if (entries == 0)
        {
            // The udta was empty or only had bytes we couldn't parse —
            // record the box itself so the user sees at least one entry.
            removed.Add(new MetadataEntry("USER_DATA", "udta", "user-data box"));
        }
    }

    /// <summary>
    /// Renders a 4-byte type as a printable string, escaping the
    /// copyright-sign byte (0xA9) so the user sees <c>©xyz</c> instead
    /// of the raw Latin-1 character.
    /// </summary>
    private static string DescribeFourcc(string type)
    {
        if (type.Length >= 1 && type[0] == (char)CopyrightSignByte)
        {
            return "©" + type[1..];
        }

        return type;
    }

    /// <summary>
    /// Decides whether a child of <c>meta</c> should be stripped. The
    /// policy matches the AVIF cleaner plus the QuickTime-specific
    /// keys / ilst pairs (which carry title / author / album / genre).
    /// </summary>
    private static bool ShouldRemoveMetaChild(
        byte[] data,
        IsoBoxReader.BoxHeader child,
        MetadataCleanOptions options,
        out string container,
        out string key,
        out string value)
    {
        container = "MP4_META";
        key = child.Type;
        value = $"{DescribeFourcc(child.Type)} box";

        switch (child.Type)
        {
            case "Exif":
                container = "EXIF";
                key = "Exif";
                value = "EXIF box";
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

                // Some other uuid box — keep it.
                container = "MP4_META";
                key = "uuid/other";
                value = "UUID box (non-EXIF, non-XMP)";
                return false;
            }

            case "colr":
            {
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

                // nclx and other colour types — keep.
                return false;
            }

            case "mime":
            {
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

            case "keys":
                container = "QUICKTIME_META";
                key = "keys";
                value = "QuickTime key namespace index";
                return true;

            case "ilst":
                container = "QUICKTIME_META";
                key = "ilst";
                value = "QuickTime title/author/album data list";
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Top-level <c>uuid</c> box: same EXIF / XMP UUID matching as the
    /// meta walker. Other uuid boxes are copied verbatim.
    /// </summary>
    private static void HandleUuidAtTopLevel(
        FileStream input,
        FileStream output,
        long pos,
        IsoBoxReader.BoxHeader header,
        MetadataCleanOptions options,
        List<MetadataEntry> removed)
    {
        if (header.PayloadSize < 16)
        {
            // Not a UUID payload — keep it.
            CopyBox(input, output, pos, header.TotalSize);
            return;
        }

        byte[] uuidBytes = new byte[16];
        input.Position = pos + header.HeaderSize;
        int read = input.Read(uuidBytes, 0, 16);
        if (read != 16)
        {
            CopyBox(input, output, pos, header.TotalSize);
            return;
        }

        var uuid = new ReadOnlySpan<byte>(uuidBytes);
        if (uuid.SequenceEqual(AppleExifUuid))
        {
            removed.Add(new MetadataEntry("EXIF", "uuid/EXIF", "EXIF data in top-level UUID box"));
            return;
        }

        if (uuid.SequenceEqual(XmpUuid))
        {
            removed.Add(new MetadataEntry("XMP", "uuid/XMP", "XMP packet in top-level UUID box"));
            return;
        }

        // Other UUID — preserve.
        CopyBox(input, output, pos, header.TotalSize);
    }

    /// <summary>
    /// In-memory walk used by <see cref="Inspect"/>. mdat is never
    /// visited because it's never a metadata carrier.
    /// </summary>
    private static void WalkForInspect(byte[] data, List<MetadataEntry> found)
    {
        int pos = 0;
        try
        {
            while (pos < data.Length)
            {
                var header = IsoBoxReader.Read(data, pos);
                switch (header.Type)
                {
                    case "ftyp":
                    case "mdat":
                    case "free":
                    case "skip":
                        // Not metadata carriers.
                        break;

                    case "moov":
                        InspectMoovChildren(data, header, found);
                        break;

                    case "meta":
                        InspectMetaChildren(data, header, found);
                        break;

                    case "udta":
                        found.Add(new MetadataEntry("USER_DATA", "udta", "user-data box (root level)"));
                        break;

                    case "uuid":
                    {
                        if (header.PayloadSize >= 16)
                        {
                            var uuid = new ReadOnlySpan<byte>(data, header.PayloadStart, 16);
                            if (uuid.SequenceEqual(AppleExifUuid))
                            {
                                found.Add(new MetadataEntry("EXIF", "uuid/EXIF", "EXIF data in top-level UUID box"));
                            }
                            else if (uuid.SequenceEqual(XmpUuid))
                            {
                                found.Add(new MetadataEntry("XMP", "uuid/XMP", "XMP packet in top-level UUID box"));
                            }
                            else
                            {
                                found.Add(new MetadataEntry("MP4_META", "uuid/other", "UUID box (non-EXIF, non-XMP)"));
                            }
                        }

                        break;
                    }

                    default:
                        // Unknown top-level — nothing to report.
                        break;
                }

                pos += header.TotalSize;
            }
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            throw new MetadataStripException("Corrupt MP4 structure encountered while inspecting.", ex);
        }
    }

    private static void InspectMoovChildren(byte[] data, IsoBoxReader.BoxHeader moov, List<MetadataEntry> found)
    {
        int childPos = moov.PayloadStart;
        int childEnd = moov.PayloadStart + moov.PayloadSize;
        while (childPos < childEnd)
        {
            var child = IsoBoxReader.Read(data, childPos);
            switch (child.Type)
            {
                case "udta":
                    InspectUdtaChildren(data, child, found);
                    break;

                case "meta":
                    InspectMetaChildren(data, child, found);
                    break;

                default:
                    // mvhd / trak / edts / mvex — structural, skip.
                    break;
            }

            childPos += child.TotalSize;
        }
    }

    private static void InspectUdtaChildren(
        byte[] data,
        IsoBoxReader.BoxHeader udta,
        List<MetadataEntry> found)
    {
        int childPos = udta.PayloadStart;
        int childEnd = udta.PayloadStart + udta.PayloadSize;
        while (childPos + 8 <= childEnd)
        {
            var child = IsoBoxReader.Read(data, childPos);
            string display = DescribeFourcc(child.Type);
            found.Add(new MetadataEntry("USER_DATA", display, "user-data child of `udta`"));
            childPos += child.TotalSize;
        }
    }

    private static void InspectMetaChildren(
        byte[] data,
        IsoBoxReader.BoxHeader meta,
        List<MetadataEntry> found)
    {
        // meta is a FullBox; children start 4 bytes after the payload start.
        if (meta.PayloadSize < 4)
        {
            return;
        }

        int childPos = meta.PayloadStart + 4;
        int childEnd = meta.PayloadStart + meta.PayloadSize;
        while (childPos + 8 <= childEnd)
        {
            var child = IsoBoxReader.Read(data, childPos);
            if (ShouldRemoveMetaChild(data, child, new MetadataCleanOptions(), out string container, out string key, out string value))
            {
                found.Add(new MetadataEntry(container, key, value));
            }

            childPos += child.TotalSize;
        }
    }
}
