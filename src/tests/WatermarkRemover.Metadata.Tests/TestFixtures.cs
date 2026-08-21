using System.Buffers.Binary;
using System.Text;

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
