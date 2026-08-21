using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Metadata;
using Xunit;

namespace WatermarkRemover.Metadata.Tests;

public class MetadataCleanerTests : IDisposable
{
    private readonly string _dir = TestFixtures.NewTempDir();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // ignore cleanup failures
        }
    }

    [Fact]
    public void Png_Inspect_FindsTextChunk()
    {
        string path = Path.Combine(_dir, "a.png");
        TestFixtures.WritePngWithText(path, "Comment", "made-by-ai");

        var cleaner = new PngMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Key == "tEXt");
    }

    [Fact]
    public void Png_Clean_RemovesTextChunk_PreservesImageData()
    {
        string input = Path.Combine(_dir, "in.png");
        string output = Path.Combine(_dir, "out.png");
        TestFixtures.WritePngWithText(input, "Comment", "made-by-ai");

        var cleaner = new PngMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Key == "tEXt");
        // Cleaning again finds nothing.
        cleaner.Inspect(output).Should().NotContain(e => e.Key == "tEXt");
        // IDAT preserved -> file still a valid PNG that the cleaner accepts.
        File.ReadAllBytes(output).Should().StartWith([(byte)0x89, 0x50, 0x4E, 0x47]);
    }

    [Fact]
    public void Png_CorruptFile_ThrowsMetadataStripException()
    {
        string path = Path.Combine(_dir, "bad.png");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);

        var cleaner = new PngMetadataCleaner();
        Action act = () => cleaner.Inspect(path);

        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Jpeg_Clean_RemovesExifApp1()
    {
        string input = Path.Combine(_dir, "in.jpg");
        string output = Path.Combine(_dir, "out.jpg");
        TestFixtures.WriteJpegWithExif(input);

        var cleaner = new JpegMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().NotBeEmpty();
        File.ReadAllBytes(output).Should().StartWith([(byte)0xFF, 0xD8]);
    }

    [Fact]
    public void Html_Clean_RemovesGeneratorMeta()
    {
        string input = Path.Combine(_dir, "in.html");
        string output = Path.Combine(_dir, "out.html");
        TestFixtures.WriteHtml(input);

        var cleaner = new HtmlMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        string cleaned = File.ReadAllText(output);
        cleaned.Should().Contain("Visible content.");
        cleaned.Should().NotContain("AI-Writer");
        result.RemovedEntries.Should().NotBeEmpty();
    }

    [Fact]
    public void Cleaner_MissingFile_Throws()
    {
        var cleaner = new PngMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.png"));

        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void WebP_Inspect_FindsAllMetadataChunks()
    {
        string path = Path.Combine(_dir, "in.webp");
        TestFixtures.WriteWebPWithMetadata(path);

        var cleaner = new WebPMetadataCleaner();
        // Default options preserve ICCP, so expect only EXIF + XMP from the default Inspect.
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Select(e => e.Key).Should().Contain(["EXIF", "XMP "]);
    }

    [Fact]
    public void WebP_Clean_StripsExifXmpIcc_KeepsVp8X_UpdatesFlags()
    {
        string input = Path.Combine(_dir, "in.webp");
        string output = Path.Combine(_dir, "out.webp");
        TestFixtures.WriteWebPWithMetadata(input);

        var cleaner = new WebPMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult result = cleaner.Clean(input, output, options);

        // EXIF, XMP and ICCP were all removed.
        result.RemovedEntries.Select(e => e.Key).Should().Contain(["EXIF", "XMP ", "ICCP"]);

        // The output is still a valid WebP container.
        byte[] outputBytes = File.ReadAllBytes(output);
        outputBytes.Should().StartWith([(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        Encoding.ASCII.GetString(outputBytes, 8, 4).Should().Be("WEBP");

        // A second pass with the same options finds no metadata chunks left to strip.
        cleaner.Clean(output, Path.Combine(_dir, "out2.webp"), options)
            .RemovedEntries.Should().BeEmpty();

        // VP8X flags are updated: EXIF (0x08), XMP (0x04) and ICC (0x20) bits are all cleared.
        int vp8xFlagPos = FindChunkDataOffset(outputBytes, "VP8X");
        vp8xFlagPos.Should().BeGreaterThan(-1);
        byte vp8xFlag = outputBytes[vp8xFlagPos];
        (vp8xFlag & 0x08).Should().Be(0, "EXIF flag should be cleared");
        (vp8xFlag & 0x04).Should().Be(0, "XMP flag should be cleared");
        (vp8xFlag & 0x20).Should().Be(0, "ICC flag should be cleared");
    }

    [Fact]
    public void WebP_Clean_WithoutVp8X_StripsMetadata_StillValidContainer()
    {
        // Some (technically out-of-spec) WebP files may carry EXIF chunks without a VP8X header.
        string input = Path.Combine(_dir, "no-vp8x.webp");
        TestFixtures.WriteWebPWithMetadata(input, includeVp8x: false);

        string output = Path.Combine(_dir, "no-vp8x-out.webp");
        var cleaner = new WebPMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult result = cleaner.Clean(input, output, options);

        result.RemovedEntries.Select(e => e.Key).Should().Contain(["EXIF", "XMP ", "ICCP"]);

        // RIFF size field equals file size minus 8.
        byte[] outputBytes = File.ReadAllBytes(output);
        uint riffSize = BitConverter.ToUInt32(outputBytes, 4);
        riffSize.Should().Be((uint)outputBytes.Length - 8);

        // No metadata chunks remain.
        cleaner.Clean(output, Path.Combine(_dir, "no-vp8x-out2.webp"), options)
            .RemovedEntries.Should().BeEmpty();
    }

    [Fact]
    public void WebP_Clean_DefaultOptions_PreservesColorProfile()
    {
        // Default MetadataCleanOptions has PreserveColorProfile = true, so ICCP must survive.
        string input = Path.Combine(_dir, "icc.webp");
        TestFixtures.WriteWebPWithMetadata(input);

        string output = Path.Combine(_dir, "icc-out.webp");
        var cleaner = new WebPMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Select(e => e.Key).Should().Contain(["EXIF", "XMP "]);
        result.RemovedEntries.Should().NotContain(e => e.Key == "ICCP");
    }

    [Fact]
    public void WebP_Inspect_CorruptFile_Throws()
    {
        string path = Path.Combine(_dir, "bad.webp");
        File.WriteAllBytes(path, "NOT-A-WEBP-FILE"u8.ToArray());

        var cleaner = new WebPMetadataCleaner();
        Action act = () => cleaner.Inspect(path);

        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void WebP_Inspect_TruncatedChunk_Throws()
    {
        // RIFF + WEBP header followed by a chunk whose size extends past EOF.
        string path = Path.Combine(_dir, "truncated.webp");
        using (var fs = File.Create(path))
        {
            fs.Write("RIFF"u8);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, 100);
            fs.Write(size);
            fs.Write("WEBP"u8);
            // "EXIF" FourCC + declared size of 80 bytes but we only write 4 bytes.
            fs.Write("EXIF"u8);
            BinaryPrimitives.WriteUInt32LittleEndian(size, 80);
            fs.Write(size);
        }

        var cleaner = new WebPMetadataCleaner();
        Action act = () => cleaner.Inspect(path);

        act.Should().Throw<MetadataStripException>();
    }

    /// <summary>Locates the first data byte of the named RIFF chunk, or -1 if not present.</summary>
    private static int FindChunkDataOffset(byte[] data, string fourcc)
    {
        // Skip RIFF + size + WEBP (12 bytes).
        int pos = 12;
        byte[] needle = Encoding.ASCII.GetBytes(fourcc);
        while (pos + 8 <= data.Length)
        {
            bool match = true;
            for (int i = 0; i < 4; i++)
            {
                if (data[pos + i] != needle[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return pos + 8;
            }

            uint size = BitConverter.ToUInt32(data, pos + 4);
            int padded = (int)((size + 1) & ~1u);
            pos += 8 + padded;
        }

        return -1;
    }
}
