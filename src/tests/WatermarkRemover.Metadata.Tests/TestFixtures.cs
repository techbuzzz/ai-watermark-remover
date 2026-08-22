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
