using System.Buffers.Binary;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace WatermarkRemover.Metadata.Tests;

/// <summary>Builds small, valid on-disk fixtures (PNG / JPEG / HTML) for metadata tests.</summary>
internal static class TestFixtures
{
    /// <summary>Create a temp directory unique to a test and return its path.</summary>
    public static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wr-md-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Writes a minimal but structurally valid PNG containing IHDR + a <c>tEXt</c> chunk +
    /// IDAT + IEND to <paramref name="path"/>. The tEXt chunk is the metadata that cleaners strip.
    /// </summary>
    public static void WritePngWithText(string path, string keyword, string value)
    {
        using FileStream fs = File.Create(path);
        // PNG signature.
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR: 1x1, 8-bit, colour type 2 (truecolour).
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), 1); // width
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), 1); // height
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type
        WriteChunk(fs, "IHDR", ihdr);

        // tEXt chunk: keyword \0 value.
        byte[] text = [.. Encoding.ASCII.GetBytes(keyword), 0x00, .. Encoding.ASCII.GetBytes(value)];
        WriteChunk(fs, "tEXt", text);

        // Minimal IDAT (not a real compressed scanline, but structurally a chunk that must survive).
        WriteChunk(fs, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]);

        // IEND.
        WriteChunk(fs, "IEND", []);
    }

    /// <summary>Writes a minimal JPEG with SOI + APP1 (Exif) + EOI.</summary>
    public static void WriteJpegWithExif(string path)
    {
        using FileStream fs = File.Create(path);
        fs.Write([0xFF, 0xD8]); // SOI

        byte[] exifPayload = [.. Encoding.ASCII.GetBytes("Exif"), 0x00, 0x00, .. Encoding.ASCII.GetBytes("Make=TestCam")];
        int segLen = exifPayload.Length + 2; // length field includes itself
        fs.Write([0xFF, 0xE1, (byte)(segLen >> 8), (byte)(segLen & 0xFF)]);
        fs.Write(exifPayload);

        fs.Write([0xFF, 0xD9]); // EOI
    }

    /// <summary>Writes an HTML document with metadata-bearing meta/comment nodes.</summary>
    public static void WriteHtml(string path)
    {
        const string html =
            """
            <!DOCTYPE html>
            <html>
            <head>
                <meta name="generator" content="AI-Writer 1.0">
                <meta name="author" content="Robot">
                <title>Sample</title>
            </head>
            <body>
                <!-- generated-by: assistant -->
                <p>Visible content.</p>
            </body>
            </html>
            """;
        File.WriteAllText(path, html);
    }

    /// <summary>
    /// Writes a minimal but structurally valid WebP file with optional VP8X, EXIF, XMP and
    /// ICCP chunks. The VP8 bitstream is just a header stub; tests don't decode pixels, they
    /// only verify metadata chunk removal.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="includeVp8x">When true, emit a VP8X chunk whose flags reflect the metadata chunks present.</param>
    /// <param name="includeExif">When true, emit an EXIF chunk (and set the EXIF flag in VP8X).</param>
    /// <param name="includeXmp">When true, emit an XMP chunk (and set the XMP flag in VP8X).</param>
    /// <param name="includeIccp">When true, emit an ICCP chunk (and set the ICC flag in VP8X).</param>
    public static void WriteWebPWithMetadata(
        string path,
        bool includeVp8x = true,
        bool includeExif = true,
        bool includeXmp = true,
        bool includeIccp = true)
    {
        using var fs = File.Create(path);

        // RIFF header: FourCC + size (patched at the end) + WEBP form-type.
        long riffSizePos = fs.Position;
        fs.Write("RIFF"u8);
        fs.Position += 4; // placeholder for file size
        fs.Write("WEBP"u8);

        if (includeVp8x)
        {
            byte flags = 0;
            if (includeExif)
            {
                flags |= 0x08;
            }

            if (includeXmp)
            {
                flags |= 0x04;
            }

            if (includeIccp)
            {
                flags |= 0x20;
            }

            // VP8X data = 4-byte flags + 3-byte canvas width-1 + 3-byte canvas height-1 = 10 bytes
            // Canvas: 1x1 (encoded as 0).
            byte[] vp8xData = [flags, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            WriteRiffChunk(fs, "VP8X", vp8xData);
        }

        // Minimal VP8 bitstream header (not a real frame, but a valid chunk that must survive).
        WriteRiffChunk(fs, "VP8 ", [0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00, 0x01, 0x40, 0x00]);

        if (includeIccp)
        {
            WriteRiffChunk(fs, "ICCP", "iccp-payload"u8.ToArray());
        }

        if (includeExif)
        {
            WriteRiffChunk(fs, "EXIF", "exif-payload"u8.ToArray());
        }

        if (includeXmp)
        {
            WriteRiffChunk(fs, "XMP ", "xmp-payload"u8.ToArray());
        }

        // Patch the RIFF size: total file size minus 8 (RIFF + size field).
        long finalPos = fs.Position;
        fs.Position = riffSizePos + 4;
        Span<byte> sizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)(finalPos - 8));
        fs.Write(sizeBytes);
    }

    /// <summary>
    /// Writes a minimal but valid TIFF file containing an EXIF profile (Make = "TestCam") to
    /// <paramref name="path"/>. The image data is an 8x8 opaque RGBA block — pixel content is
    /// not the focus, the metadata is.
    /// </summary>
    public static void WriteTiffWithExif(string path)
    {
        // ImageSharp's TIFF encoder writes EXIF tags inline in IFD0 (not as a sub-IFD
        // pointed to by tag 0x8769), so its own decoder can't read them back. We hand-craft
        // a spec-compliant TIFF with a proper EXIF sub-IFD that ImageSharp can parse.
        File.WriteAllBytes(path, BuildHandCraftedTiffWithExif());
    }

    /// <summary>
    /// Builds a minimal little-endian 8-bit grayscale TIFF with a proper EXIF sub-IFD
    /// containing Make = "TestCam". Image data is an 8x8 block of zeros (strip-packed).
    /// </summary>
    public static byte[] BuildHandCraftedTiffWithExif()
    {
        // IFD0 layout (10 entries, all in ascending tag order):
        //   0x0100 ImageWidth           SHORT  1  8
        //   0x0101 ImageLength          SHORT  1  8
        //   0x0102 BitsPerSample        SHORT  1  8
        //   0x0103 Compression          SHORT  1  1   (no compression)
        //   0x0106 PhotometricInterp    SHORT  1  1   (BlackIsZero)
        //   0x0111 StripOffsets         LONG   1  <strip offset>
        //   0x0115 SamplesPerPixel      SHORT  1  1
        //   0x0116 RowsPerStrip         SHORT  1  8
        //   0x0117 StripByteCounts      LONG   1  64
        //   0x8769 ExifIFD              LONG   1  <exif ifd offset>
        const int ifd0EntryCount = 10;
        const int ifd0Size = 2 + (ifd0EntryCount * 12) + 4;
        const int stripOffset = 8 + ifd0Size;
        const int stripSize = 8 * 8; // 8x8 grayscale
        const int exifIfdOffset = stripOffset + stripSize;
        const int exifIfdEntryCount = 1;
        const int exifIfdSize = 2 + (exifIfdEntryCount * 12) + 4;
        const int makeStringOffset = exifIfdOffset + exifIfdSize;

        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Header (8 bytes).
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((ushort)42);
        bw.Write((uint)8);

        // IFD0.
        bw.Write((ushort)ifd0EntryCount);

        WriteIfdEntry(bw, 0x0100, 3, 1, inline: 8);
        WriteIfdEntry(bw, 0x0101, 3, 1, inline: 8);
        WriteIfdEntry(bw, 0x0102, 3, 1, inline: 8);
        WriteIfdEntry(bw, 0x0103, 3, 1, inline: 1);
        WriteIfdEntry(bw, 0x0106, 3, 1, inline: 1);
        WriteIfdEntry(bw, 0x0111, 4, 1, offset: (uint)stripOffset);
        WriteIfdEntry(bw, 0x0115, 3, 1, inline: 1);
        WriteIfdEntry(bw, 0x0116, 3, 1, inline: 8);
        WriteIfdEntry(bw, 0x0117, 4, 1, inline: 64);
        WriteIfdEntry(bw, 0x8769, 4, 1, offset: (uint)exifIfdOffset);

        bw.Write((uint)0); // next IFD offset

        // Strip data (64 bytes of zeros).
        bw.Write(new byte[stripSize]);

        // EXIF sub-IFD.
        bw.Write((ushort)exifIfdEntryCount);
        WriteIfdEntry(bw, 0x010F, 2, 8, offset: (uint)makeStringOffset); // Make, ASCII, 8 bytes
        bw.Write((uint)0); // next IFD offset

        // Make string (7 chars + null terminator).
        bw.Write("TestCam\0"u8);

        return ms.ToArray();
    }

    private static void WriteIfdEntry(BinaryWriter bw, ushort tag, ushort type, uint count, uint inline = 0, uint offset = 0)
    {
        // For SHORT/ASCII with count <= 2, the value fits in 4 bytes inline.
        // For LONG with count == 1, the value fits in 4 bytes inline.
        // Otherwise, the value is an offset (only `offset` is set in that case by the caller).
        bw.Write(tag);
        bw.Write(type);
        bw.Write(count);
        bw.Write(inline != 0 ? inline : offset);
    }

    /// <summary>
    /// Writes a minimal but valid TIFF file with no metadata profiles attached. The image data
    /// is an 8x8 opaque RGBA block.
    /// </summary>
    public static void WriteBareTiff(string path)
    {
        using var image = new Image<Rgba32>(8, 8);
        image.SaveAsTiff(path);
    }

    private static void WriteRiffChunk(Stream stream, string fourcc, byte[] data)
    {
        byte[] fourccBytes = Encoding.ASCII.GetBytes(fourcc);
        stream.Write(fourccBytes);

        Span<byte> sizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)data.Length);
        stream.Write(sizeBytes);

        stream.Write(data);

        // RIFF chunks are padded to an even data section.
        if ((data.Length & 1) == 1)
        {
            stream.WriteByte(0);
        }
    }

    /// <summary>
    /// Writes a minimal but structurally valid HEIF / HEIC file (ISOBMFF) with the requested
    /// metadata children inside the <c>meta</c> box. The file is "playable" in the sense that
    /// <see cref="HeifMetadataCleaner"/> will accept it and find the metadata boxes; the actual
    /// HEVC bitstream is just a stub byte sequence — the tests don't decode pixels.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="brand">The major_brand for the ftyp box (e.g. "heic", "mif1", "jpeg").</param>
    /// <param name="includeHdlr">When true, emit a structural <c>hdlr</c> box as the first child of <c>meta</c>.</param>
    /// <param name="includePitm">When true, emit a structural <c>pitm</c> box after <c>hdlr</c>.</param>
    /// <param name="includeExif4cc">When true, emit a 4CC <c>Exif</c> box carrying an "Exif\0\0" stub payload.</param>
    /// <param name="includeExifUuid">When true, emit a <c>uuid</c> box with the Apple EXIF UUID.</param>
    /// <param name="includeXmpUuid">When true, emit a <c>uuid</c> box with the XMP UUID.</param>
    /// <param name="includeIccColr">When true, emit a <c>colr</c> box of colour type <c>rICC</c>.</param>
    /// <param name="includeNclxColr">When true, emit a <c>colr</c> box of colour type <c>nclx</c> (kept verbatim by the cleaner).</param>
    /// <param name="includeMimeXmp">When true, emit a <c>mime</c> box whose content_type is <c>application/rdf+xml</c>.</param>
    /// <param name="useLargeSize">When true, wrap the mdat box in an 8-byte <c>largesize</c> extension (size == 1).</param>
    public static void WriteHeifWithMetadata(
        string path,
        string brand = "heic",
        bool includeHdlr = true,
        bool includePitm = true,
        bool includeExif4cc = true,
        bool includeExifUuid = false,
        bool includeXmpUuid = true,
        bool includeIccColr = true,
        bool includeNclxColr = false,
        bool includeMimeXmp = false,
        bool useLargeSize = false)
    {
        using var fs = File.Create(path);

        // ftyp box: size(4) + "ftyp" + major_brand(4) + minor_version(4) + compatible_brands(4)
        Span<byte> ftypSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(ftypSize, 20u);
        fs.Write(ftypSize);
        fs.Write("ftyp"u8);
        fs.Write(Encoding.ASCII.GetBytes(brand));
        fs.Write([0x00, 0x00, 0x00, 0x00]); // minor_version
        fs.Write("mif1"u8); // compatible_brand

        // Build the meta box payload in a separate MemoryStream so we can
        // patch the meta box's total size after we know the children's sizes.
        using var metaChildren = new MemoryStream();

        if (includeHdlr)
        {
            // hdlr FullBox: 8 header + 4 version+flags + 4 pre_defined + 4 handler_type + 12 reserved + 1 name nul
            WriteIsoBox(metaChildren, "hdlr", BuildHdlrPayload());
        }

        if (includePitm)
        {
            // pitm FullBox: 8 header + 4 version+flags + 2 item_id
            const int pitmSize = 8 + 4 + 2;
            Span<byte> pitmPayload = stackalloc byte[4 + 2];
            pitmPayload[0] = 0; pitmPayload[1] = 0; pitmPayload[2] = 0; pitmPayload[3] = 0; // version + flags
            BinaryPrimitives.WriteUInt16BigEndian(pitmPayload.Slice(4, 2), 1); // item_id = 1
            WriteIsoBoxRaw(metaChildren, pitmSize, "pitm", pitmPayload);
        }

        if (includeExif4cc)
        {
            // 4CC Exif box: 8 header + 8 bytes of stub "Exif\0\0" + extra IFD stub
            byte[] exifPayload = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00, (byte)'I', (byte)'I', 0x00, 0x2A, 0x00, 0x00];
            WriteIsoBox(metaChildren, "Exif", exifPayload);
        }

        if (includeExifUuid)
        {
            // uuid box carrying the Apple EXIF UUID + 8 bytes of stub TIFF header.
            byte[] uuidPayload = new byte[16 + 8];
            byte[] appleExifUuid =
            [
                0x85, 0x32, 0xC9, 0xA2, 0x3B, 0x9A, 0x11, 0xE4,
                0xB6, 0xA2, 0x04, 0x01, 0xE0, 0xCB, 0xBF, 0xCE,
            ];
            Array.Copy(appleExifUuid, uuidPayload, 16);
            uuidPayload[16] = (byte)'I'; uuidPayload[17] = (byte)'I';
            uuidPayload[18] = 0x2A; uuidPayload[19] = 0x00;
            uuidPayload[20] = 0x08; uuidPayload[21] = 0x00; uuidPayload[22] = 0x00; uuidPayload[23] = 0x00;
            WriteIsoBox(metaChildren, "uuid", uuidPayload);
        }

        if (includeXmpUuid)
        {
            // uuid box carrying the XMP UUID + a tiny XMP packet stub.
            byte[] uuidPayload = new byte[16 + 8];
            byte[] xmpUuid =
            [
                0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8,
                0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC,
            ];
            Array.Copy(xmpUuid, uuidPayload, 16);
            byte[] xmpStub = "<?xpkt?>"u8.ToArray();
            Array.Copy(xmpStub, 0, uuidPayload, 16, xmpStub.Length);
            WriteIsoBox(metaChildren, "uuid", uuidPayload);
        }

        if (includeIccColr)
        {
            // colr box: 8 header + 4 colour_type ("rICC") + 8 bytes of stub ICC data.
            byte[] colrPayload = new byte[4 + 8];
            colrPayload[0] = (byte)'r'; colrPayload[1] = (byte)'I'; colrPayload[2] = (byte)'C'; colrPayload[3] = (byte)'C';
            WriteIsoBox(metaChildren, "colr", colrPayload);
        }

        if (includeNclxColr)
        {
            // colr box of colour_type "nclx" (kept verbatim by the cleaner).
            byte[] colrPayload = new byte[4 + 12];
            colrPayload[0] = (byte)'n'; colrPayload[1] = (byte)'c'; colrPayload[2] = (byte)'l'; colrPayload[3] = (byte)'x';
            WriteIsoBox(metaChildren, "colr", colrPayload);
        }

        if (includeMimeXmp)
        {
            // mime box: 8 header + null-terminated content_type + stub XMP packet.
            byte[] mimeContent = "application/rdf+xml"u8.ToArray();
            byte[] stub = "<x:xmpmeta/>"u8.ToArray();
            byte[] payload = new byte[mimeContent.Length + 1 + stub.Length];
            Array.Copy(mimeContent, payload, mimeContent.Length);
            payload[mimeContent.Length] = 0x00; // nul terminator
            Array.Copy(stub, 0, payload, mimeContent.Length + 1, stub.Length);
            WriteIsoBox(metaChildren, "mime", payload);
        }

        // Build the meta box (FullBox) = 8 header + 4 version+flags + children.
        byte[] childrenBytes = metaChildren.ToArray();
        long metaTotalSize = 8L + 4 + childrenBytes.Length;
        if (metaTotalSize > uint.MaxValue)
        {
            throw new InvalidOperationException("meta box exceeds 4 GiB.");
        }

        Span<byte> metaSizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(metaSizeBytes, (uint)metaTotalSize);
        fs.Write(metaSizeBytes);
        fs.Write("meta"u8);
        fs.Write([0x00, 0x00, 0x00, 0x00]); // version 0, flags 0
        fs.Write(childrenBytes, 0, childrenBytes.Length);

        // mdat box (image bitstream stub).
        byte[] mdatPayload = [0x00, 0x00, 0x00, 0x04, (byte)'h', (byte)'v', (byte)'c', (byte)'1']; // pretend NAL header
        if (useLargeSize)
        {
            long mdatTotal = 16L + mdatPayload.Length; // 16-byte largesize header
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 1u); // size == 1 signals largesize
            fs.Write(hdr);
            fs.Write("mdat"u8);
            Span<byte> large = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(large, mdatTotal);
            fs.Write(large);
            fs.Write(mdatPayload, 0, mdatPayload.Length);
        }
        else
        {
            WriteIsoBox(fs, "mdat", mdatPayload);
        }
    }

    private static void WriteIsoBox(Stream stream, string type, byte[] payload)
    {
        long total = 8L + payload.Length;
        if (total > uint.MaxValue)
        {
            throw new InvalidOperationException($"Box {type} exceeds 4 GiB.");
        }

        Span<byte> hdr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)total);
        stream.Write(hdr);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteIsoBoxRaw(Stream stream, int totalSize, string type, ReadOnlySpan<byte> payload)
    {
        if (totalSize != 8 + payload.Length)
        {
            throw new ArgumentException("totalSize must equal 8 + payload.Length for raw box writer.");
        }

        Span<byte> hdr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)totalSize);
        stream.Write(hdr);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
    }

    private static byte[] BuildHdlrPayload()
    {
        // hdlr FullBox payload: version(1) + flags(3) + pre_defined(4) + handler_type(4) + reserved(12) + name(nul)
        var payload = new byte[4 + 4 + 4 + 12 + 1];
        // version = 0, flags = 0 (already zeros)
        // pre_defined = 0 (already zeros)
        payload[8] = (byte)'p';
        payload[9] = (byte)'i';
        payload[10] = (byte)'c';
        payload[11] = (byte)'t';
        // reserved = 0 (already zeros)
        // name = "" with a single nul terminator at the last byte (already zero)
        return payload;
    }

    /// <summary>
    /// Parses the children of the <c>meta</c> box in a HEIF byte sequence. Returns the
    /// 4CC type of each child plus, for <c>uuid</c> boxes, the 16-byte UUID as a
    /// lowercase hex string. Used by tests to assert which metadata children survived
    /// the cleaner's pass.
    /// </summary>
    public static IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)>
        ReadMetaChildren(byte[] data)
    {
        var result = new List<(string, string?, string?, string?)>();

        // Locate the meta box.
        int pos = 0;
        while (pos + 8 <= data.Length)
        {
            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            string type = Encoding.ASCII.GetString(data, pos + 4, 4);
            int headerSize;
            int boxSize;
            if (size32 == 1)
            {
                long large = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(pos + 8, 8));
                headerSize = 16;
                boxSize = (int)large;
            }
            else
            {
                headerSize = 8;
                boxSize = (int)size32;
            }

            if (type == "meta")
            {
                int payloadStart = pos + headerSize + 4; // skip version + flags
                int payloadEnd = pos + boxSize;
                int childPos = payloadStart;
                while (childPos + 8 <= payloadEnd)
                {
                    uint cSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(childPos, 4));
                    string cType = Encoding.ASCII.GetString(data, childPos + 4, 4);
                    int cHeaderSize = 8;
                    int cBoxSize = (int)cSize;
                    if (cBoxSize < 8)
                    {
                        break;
                    }

                    int cPayloadStart = childPos + cHeaderSize;
                    int cPayloadSize = cBoxSize - cHeaderSize;

                    string? uuid = null;
                    string? colourType = null;
                    string? mimeType = null;
                    if (cType == "uuid" && cPayloadSize >= 16)
                    {
                        uuid = Convert.ToHexString(data, cPayloadStart, 16).ToLowerInvariant();
                    }
                    else if (cType == "colr" && cPayloadSize >= 4)
                    {
                        colourType = Encoding.ASCII.GetString(data, cPayloadStart, 4);
                    }
                    else if (cType == "mime")
                    {
                        int limit = Math.Min(cPayloadSize, 64);
                        int nulAt = -1;
                        for (int i = 0; i < limit; i++)
                        {
                            if (data[cPayloadStart + i] == 0)
                            {
                                nulAt = i;
                                break;
                            }
                        }

                        mimeType = nulAt < 0
                            ? Encoding.ASCII.GetString(data, cPayloadStart, limit)
                            : Encoding.ASCII.GetString(data, cPayloadStart, nulAt);
                    }

                    result.Add((cType, uuid, colourType, mimeType));
                    childPos += cBoxSize;
                }

                return result;
            }

            pos += boxSize;
        }

        return result;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        // CRC over type + data.
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }
}
