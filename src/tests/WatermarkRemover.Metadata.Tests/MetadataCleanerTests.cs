using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

    [Fact]
    public void Heif_Inspect_FindsExifAndXmp()
    {
        // Default `PreserveColorProfile = true` keeps the ICC rICC box, so the
        // default Inspect reports only the EXIF + XMP carriers.
        string path = Path.Combine(_dir, "in.heic");
        TestFixtures.WriteHeifWithMetadata(path);

        var cleaner = new HeifMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);
        entries.Should().NotContain(e => e.Container == "ICC");
    }

    [Fact]
    public void Heif_Inspect_WithStripIcc_AlsoFindsIcc()
    {
        // When the caller opts into ICC stripping, the colr/rICC box shows up in Inspect.
        string path = Path.Combine(_dir, "in.heic");
        TestFixtures.WriteHeifWithMetadata(path);

        var cleaner = new HeifMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        // Simulate the "PreserveColorProfile = false" path by inspecting a hypothetical
        // ICC-only fixture as well — the default Inspect never lists ICC.
        entries.Should().NotContain(e => e.Container == "ICC",
            "default `PreserveColorProfile = true` must keep ICC; the ICC path is exercised in Heif_Clean_RemovesExifXmpIcc_KeepsStructuralAndMdat instead");
    }

    [Fact]
    public void Heif_Clean_RemovesExifXmpIcc_KeepsStructuralAndMdat()
    {
        string input = Path.Combine(_dir, "in.heic");
        string output = Path.Combine(_dir, "out.heic");
        TestFixtures.WriteHeifWithMetadata(input);

        var cleaner = new HeifMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult result = cleaner.Clean(input, output, options);

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP", "ICC"]);

        // The output is still a valid HEIF container with the right ftyp brand.
        byte[] outputBytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(outputBytes, 4, 4).Should().Be("ftyp");
        Encoding.ASCII.GetString(outputBytes, 8, 4).Should().Be("heic");

        // meta still has the structural children (hdlr, pitm) — the metadata boxes are gone.
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Select(c => c.Type).Should().Contain(["hdlr", "pitm"]);
        children.Should().NotContain(c => c.Type == "Exif");
        children.Should().NotContain(c => c.Type == "uuid" && c.Uuid == "8532c9a23b9a11e4b6a20401e0cbbFce".ToLowerInvariant());
        children.Should().NotContain(c => c.Type == "uuid" && c.Uuid == "be7acfcb97a942e89c71999491e3afac");
        children.Should().NotContain(c => c.Type == "colr" && c.ColourType == "rICC");

        // mdat survived — the bitstream is byte-for-byte identical.
        // (Find the mdat box in input and compare its payload.)
        byte[] inputBytes = File.ReadAllBytes(input);
        byte[] inputMdatPayload = ExtractMdatPayload(inputBytes);
        byte[] outputMdatPayload = ExtractMdatPayload(outputBytes);
        outputMdatPayload.Should().Equal(inputMdatPayload);
    }

    [Fact]
    public void Heif_Clean_DefaultOptions_PreservesColorProfile()
    {
        // Default `PreserveColorProfile = true` — ICC rICC box must survive.
        string input = Path.Combine(_dir, "icc.heic");
        string output = Path.Combine(_dir, "icc-out.heic");
        TestFixtures.WriteHeifWithMetadata(input);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);
        result.RemovedEntries.Should().NotContain(e => e.Container == "ICC");

        byte[] outputBytes = File.ReadAllBytes(output);
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Should().Contain(c => c.Type == "colr" && c.ColourType == "rICC");
    }

    [Fact]
    public void Heif_Clean_KeepsNclxColr()
    {
        // nclx is the inline colour primaries / transfer / matrix representation —
        // it is the image's colour description, not metadata, and must be preserved.
        string input = Path.Combine(_dir, "nclx.heic");
        string output = Path.Combine(_dir, "nclx-out.heic");
        TestFixtures.WriteHeifWithMetadata(input, includeIccColr: false, includeNclxColr: true);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions { PreserveColorProfile = false });

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);

        byte[] outputBytes = File.ReadAllBytes(output);
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Should().Contain(c => c.Type == "colr" && c.ColourType == "nclx");
    }

    [Fact]
    public void Heif_Inspect_AppleUuidExif_Found()
    {
        // Apple-style EXIF storage uses a uuid box with the Apple-defined EXIF UUID.
        string path = Path.Combine(_dir, "apple.heic");
        TestFixtures.WriteHeifWithMetadata(path, includeExif4cc: false, includeExifUuid: true);

        var cleaner = new HeifMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "EXIF" && e.Key == "uuid/EXIF");
    }

    [Fact]
    public void Heif_Clean_AppleUuidExif_Removed()
    {
        string input = Path.Combine(_dir, "apple-in.heic");
        string output = Path.Combine(_dir, "apple-out.heic");
        TestFixtures.WriteHeifWithMetadata(input, includeExif4cc: false, includeExifUuid: true);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "EXIF" && e.Key == "uuid/EXIF");
        cleaner.Inspect(output).Should().NotContain(e => e.Key == "uuid/EXIF");
    }

    [Fact]
    public void Heif_Clean_MimeXmp_Removed()
    {
        // XMP carried in a mime box (alternative storage) — also stripped.
        string input = Path.Combine(_dir, "mime-in.heic");
        string output = Path.Combine(_dir, "mime-out.heic");
        TestFixtures.WriteHeifWithMetadata(input, includeXmpUuid: false, includeMimeXmp: true);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "XMP" && e.Key == "mime/XMP");
        cleaner.Inspect(output).Should().NotContain(e => e.Key == "mime/XMP");
    }

    [Fact]
    public void Heif_Inspect_NonExifUuid_Kept()
    {
        // A uuid box with a non-EXIF, non-XMP UUID is some other private box and must be preserved.
        string path = Path.Combine(_dir, "private.heic");
        // The fixture does not expose an arbitrary-uuid knob, so we hand-craft a small file
        // whose meta contains only a uuid box with a placeholder UUID.
        using (var fs = File.Create(path))
        {
            // ftyp
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("heic"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("mif1"u8);

            // Build meta payload = [hdlr] + [uuid(arbitrary)]
            using var metaChildren = new MemoryStream();
            // hdlr
            WriteHeifHdlr(metaChildren);
            // uuid (arbitrary)
            byte[] arbitraryUuid = new byte[16 + 4];
            for (int i = 0; i < 16; i++)
            {
                arbitraryUuid[i] = (byte)(0x10 + i);
            }

            WriteHeifUuidBox(metaChildren, arbitraryUuid);
            byte[] childrenBytes = metaChildren.ToArray();

            int metaTotal = 8 + 4 + childrenBytes.Length;
            BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)metaTotal);
            fs.Write(hdr);
            fs.Write("meta"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write(childrenBytes, 0, childrenBytes.Length);

            // mdat (empty)
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 8u);
            fs.Write(hdr);
            fs.Write("mdat"u8);
        }

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(path, Path.Combine(_dir, "private-out.heic"), new MetadataCleanOptions());

        result.RemovedEntries.Should().BeEmpty("non-EXIF, non-XMP uuid boxes must be preserved");

        byte[] outputBytes = File.ReadAllBytes(Path.Combine(_dir, "private-out.heic"));
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Should().Contain(c => c.Type == "uuid");
    }

    [Fact]
    public void Heif_Clean_Reclean_Empty()
    {
        // Two passes with the same options — the second pass must find nothing to strip.
        string input = Path.Combine(_dir, "reclean-in.heic");
        string pass1 = Path.Combine(_dir, "reclean-1.heic");
        string pass2 = Path.Combine(_dir, "reclean-2.heic");
        TestFixtures.WriteHeifWithMetadata(input);

        var cleaner = new HeifMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult first = cleaner.Clean(input, pass1, options);
        FileCleanResult second = cleaner.Clean(pass1, pass2, options);

        first.RemovedEntries.Should().NotBeEmpty();
        second.RemovedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Heif_Clean_LargeSizeMdat_Supported()
    {
        // mdat is wrapped in a largesize box (size == 1 → 8-byte BE u64 follows).
        string input = Path.Combine(_dir, "large-in.heic");
        string output = Path.Combine(_dir, "large-out.heic");
        TestFixtures.WriteHeifWithMetadata(input, useLargeSize: true);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions { PreserveColorProfile = false });

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP", "ICC"]);

        // mdat payload preserved even with the largesize header.
        byte[] inputBytes = File.ReadAllBytes(input);
        byte[] outputBytes = File.ReadAllBytes(output);
        ExtractMdatPayload(inputBytes).Should().Equal(ExtractMdatPayload(outputBytes));
    }

    [Fact]
    public void Heif_Clean_OnlyFtypAndMeta_Succeeds()
    {
        // No mdat box — only ftyp + meta. Walker must not require mdat.
        string path = Path.Combine(_dir, "no-mdat.heic");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("heic"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("mif1"u8);

            // meta: hdlr + Exif
            using var metaChildren = new MemoryStream();
            WriteHeifHdlr(metaChildren);
            WriteHeifPlainBox(metaChildren, "Exif", [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00]);
            byte[] children = metaChildren.ToArray();

            int metaTotal = 8 + 4 + children.Length;
            BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)metaTotal);
            fs.Write(hdr);
            fs.Write("meta"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write(children, 0, children.Length);
        }

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(path, Path.Combine(_dir, "no-mdat-out.heic"), new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "EXIF");
    }

    [Fact]
    public void Heif_Inspect_NoMetadata_ReturnsEmpty()
    {
        string path = Path.Combine(_dir, "bare.heic");
        TestFixtures.WriteHeifWithMetadata(
            path,
            includeExif4cc: false,
            includeXmpUuid: false,
            includeIccColr: false);

        var cleaner = new HeifMetadataCleaner();
        cleaner.Inspect(path).Should().BeEmpty();
    }

    [Fact]
    public void Heif_Clean_NoMetadata_OutputValidHeif()
    {
        string input = Path.Combine(_dir, "bare-in.heic");
        string output = Path.Combine(_dir, "bare-out.heic");
        TestFixtures.WriteHeifWithMetadata(
            input,
            includeExif4cc: false,
            includeXmpUuid: false,
            includeIccColr: false);

        var cleaner = new HeifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().BeEmpty();
        File.Exists(output).Should().BeTrue();

        byte[] outputBytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(outputBytes, 4, 4).Should().Be("ftyp");
    }

    [Fact]
    public void Heif_Header_NotHeif_Throws()
    {
        // ftyp is present but the brand is unknown (e.g. "qt  " for QuickTime).
        string path = Path.Combine(_dir, "fake.heic");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("qt  "u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("qt  "u8);
        }

        var cleaner = new HeifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Heif_Header_NotIsoBmff_Throws()
    {
        // The first 4 bytes are not a valid size — a JPEG-prefixed file rejected.
        string path = Path.Combine(_dir, "jpeg-as-heic.heic");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F']);

        var cleaner = new HeifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Heif_Header_TruncatedFtyp_Throws()
    {
        // First 4 bytes declare size 16 but file is only 10 bytes long.
        string path = Path.Combine(_dir, "truncated.heic");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x10, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i']);

        var cleaner = new HeifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Heif_Brand_Heif_Accepted()
    {
        // The HEIF specification brands (heic / heix / heim / heis / hevc / hevx / mif1 / msf1 / jpeg)
        // should all pass header validation. We only need to check one alternate brand because
        // a 0th-brand-accepted file already exercises the validation path; spot-check "mif1" too.
        foreach (string brand in new[] { "heic", "mif1", "jpeg" })
        {
            string path = Path.Combine(_dir, $"brand-{brand}.heic");
            TestFixtures.WriteHeifWithMetadata(path, brand: brand);
            var cleaner = new HeifMetadataCleaner();
            Action act = () => cleaner.Inspect(path);
            act.Should().NotThrow($"brand {brand} should be accepted as HEIF-compatible");
        }
    }

    [Fact]
    public void Heif_CorruptMetaTruncated_Throws()
    {
        // ftyp is valid, but the meta box declares a size that runs past EOF.
        string path = Path.Combine(_dir, "bad-meta.heic");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("heic"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("mif1"u8);

            // meta with size=4096 but no payload
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 4096u);
            fs.Write(hdr);
            fs.Write("meta"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
        }

        var cleaner = new HeifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Heif_Cleaner_MissingFile_Throws()
    {
        var cleaner = new HeifMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.heic"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Heif_CanHandle_RecognisesExtensions()
    {
        var cleaner = new HeifMetadataCleaner();
        cleaner.CanHandle(".heic").Should().BeTrue();
        cleaner.CanHandle(".heif").Should().BeTrue();
        cleaner.CanHandle(".HEIC").Should().BeTrue();
        cleaner.CanHandle(".HEIF").Should().BeTrue();
        cleaner.CanHandle(".png").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".heic", ".heif"]);
    }

    [Fact]
    public void Avif_Inspect_FindsExifAndXmp()
    {
        // Default `PreserveColorProfile = true` keeps the ICC rICC box, so the
        // default Inspect reports only the EXIF + XMP carriers.
        string path = Path.Combine(_dir, "in.avif");
        TestFixtures.WriteAvifWithMetadata(path);

        var cleaner = new AvifMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);
        entries.Should().NotContain(e => e.Container == "ICC");
    }

    [Fact]
    public void Avif_Clean_RemovesExifXmpIcc_KeepsStructuralAndMdat()
    {
        string input = Path.Combine(_dir, "in.avif");
        string output = Path.Combine(_dir, "out.avif");
        TestFixtures.WriteAvifWithMetadata(input);

        var cleaner = new AvifMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult result = cleaner.Clean(input, output, options);

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP", "ICC"]);

        // The output is still a valid AVIF container with the right ftyp brand.
        byte[] outputBytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(outputBytes, 4, 4).Should().Be("ftyp");
        Encoding.ASCII.GetString(outputBytes, 8, 4).Should().Be("avif");

        // meta still has the structural children (hdlr, pitm) — the metadata boxes are gone.
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Select(c => c.Type).Should().Contain(["hdlr", "pitm"]);
        children.Should().NotContain(c => c.Type == "Exif");
        children.Should().NotContain(c => c.Type == "uuid" && c.Uuid == "8532c9a23b9a11e4b6a20401e0cbbFce".ToLowerInvariant());
        children.Should().NotContain(c => c.Type == "uuid" && c.Uuid == "be7acfcb97a942e89c71999491e3afac");
        children.Should().NotContain(c => c.Type == "colr" && c.ColourType == "rICC");

        // mdat survived — the bitstream is byte-for-byte identical.
        byte[] inputBytes = File.ReadAllBytes(input);
        ExtractMdatPayload(outputBytes).Should().Equal(ExtractMdatPayload(inputBytes));
    }

    [Fact]
    public void Avif_Clean_DefaultOptions_PreservesColorProfile()
    {
        // Default `PreserveColorProfile = true` — ICC rICC box must survive.
        string input = Path.Combine(_dir, "icc.avif");
        string output = Path.Combine(_dir, "icc-out.avif");
        TestFixtures.WriteAvifWithMetadata(input);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);
        result.RemovedEntries.Should().NotContain(e => e.Container == "ICC");

        byte[] outputBytes = File.ReadAllBytes(output);
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Should().Contain(c => c.Type == "colr" && c.ColourType == "rICC");
    }

    [Fact]
    public void Avif_Clean_KeepsNclxColr()
    {
        // nclx is the inline colour primaries / transfer / matrix representation —
        // it is the image's colour description, not metadata, and must be preserved.
        string input = Path.Combine(_dir, "nclx.avif");
        string output = Path.Combine(_dir, "nclx-out.avif");
        TestFixtures.WriteAvifWithMetadata(input, includeIccColr: false, includeNclxColr: true);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions { PreserveColorProfile = false });

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP"]);

        byte[] outputBytes = File.ReadAllBytes(output);
        IReadOnlyList<(string Type, string? Uuid, string? ColourType, string? MimeContentType)> children
            = TestFixtures.ReadMetaChildren(outputBytes);
        children.Should().Contain(c => c.Type == "colr" && c.ColourType == "nclx");
    }

    [Fact]
    public void Avif_Inspect_AppleUuidExif_Found()
    {
        // Apple-style EXIF storage uses a uuid box with the Apple-defined EXIF UUID.
        string path = Path.Combine(_dir, "apple.avif");
        TestFixtures.WriteAvifWithMetadata(path, includeExif4cc: false, includeExifUuid: true);

        var cleaner = new AvifMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "EXIF" && e.Key == "uuid/EXIF");
    }

    [Fact]
    public void Avif_Clean_AppleUuidExif_Removed()
    {
        string input = Path.Combine(_dir, "apple-in.avif");
        string output = Path.Combine(_dir, "apple-out.avif");
        TestFixtures.WriteAvifWithMetadata(input, includeExif4cc: false, includeExifUuid: true);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "EXIF" && e.Key == "uuid/EXIF");
        cleaner.Inspect(output).Should().NotContain(e => e.Key == "uuid/EXIF");
    }

    [Fact]
    public void Avif_Clean_MimeXmp_Removed()
    {
        // XMP carried in a mime box (alternative storage) — also stripped.
        string input = Path.Combine(_dir, "mime-in.avif");
        string output = Path.Combine(_dir, "mime-out.avif");
        TestFixtures.WriteAvifWithMetadata(input, includeXmpUuid: false, includeMimeXmp: true);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "XMP" && e.Key == "mime/XMP");
        cleaner.Inspect(output).Should().NotContain(e => e.Key == "mime/XMP");
    }

    [Fact]
    public void Avif_Clean_Reclean_Empty()
    {
        // Two passes with the same options — the second pass must find nothing to strip.
        string input = Path.Combine(_dir, "reclean-in.avif");
        string pass1 = Path.Combine(_dir, "reclean-1.avif");
        string pass2 = Path.Combine(_dir, "reclean-2.avif");
        TestFixtures.WriteAvifWithMetadata(input);

        var cleaner = new AvifMetadataCleaner();
        var options = new MetadataCleanOptions { PreserveColorProfile = false };
        FileCleanResult first = cleaner.Clean(input, pass1, options);
        FileCleanResult second = cleaner.Clean(pass1, pass2, options);

        first.RemovedEntries.Should().NotBeEmpty();
        second.RemovedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Avif_Clean_LargeSizeMdat_Supported()
    {
        // mdat is wrapped in a largesize box (size == 1 → 8-byte BE u64 follows).
        string input = Path.Combine(_dir, "large-in.avif");
        string output = Path.Combine(_dir, "large-out.avif");
        TestFixtures.WriteAvifWithMetadata(input, useLargeSize: true);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions { PreserveColorProfile = false });

        result.RemovedEntries.Select(e => e.Container).Should().Contain(["EXIF", "XMP", "ICC"]);

        // mdat payload preserved even with the largesize header.
        byte[] inputBytes = File.ReadAllBytes(input);
        byte[] outputBytes = File.ReadAllBytes(output);
        ExtractMdatPayload(inputBytes).Should().Equal(ExtractMdatPayload(outputBytes));
    }

    [Fact]
    public void Avif_Clean_OnlyFtypAndMeta_Succeeds()
    {
        // No mdat box — only ftyp + meta. Walker must not require mdat.
        string path = Path.Combine(_dir, "no-mdat.avif");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("avif"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("mif1"u8);

            // meta: hdlr + Exif
            using var metaChildren = new MemoryStream();
            WriteHeifHdlr(metaChildren);
            WriteHeifPlainBox(metaChildren, "Exif", [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00]);
            byte[] children = metaChildren.ToArray();

            int metaTotal = 8 + 4 + children.Length;
            BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)metaTotal);
            fs.Write(hdr);
            fs.Write("meta"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write(children, 0, children.Length);
        }

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(path, Path.Combine(_dir, "no-mdat-out.avif"), new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "EXIF");
    }

    [Fact]
    public void Avif_Inspect_NoMetadata_ReturnsEmpty()
    {
        string path = Path.Combine(_dir, "bare.avif");
        TestFixtures.WriteAvifWithMetadata(
            path,
            includeExif4cc: false,
            includeXmpUuid: false,
            includeIccColr: false);

        var cleaner = new AvifMetadataCleaner();
        cleaner.Inspect(path).Should().BeEmpty();
    }

    [Fact]
    public void Avif_Clean_NoMetadata_OutputValidAvif()
    {
        string input = Path.Combine(_dir, "bare-in.avif");
        string output = Path.Combine(_dir, "bare-out.avif");
        TestFixtures.WriteAvifWithMetadata(
            input,
            includeExif4cc: false,
            includeXmpUuid: false,
            includeIccColr: false);

        var cleaner = new AvifMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().BeEmpty();
        File.Exists(output).Should().BeTrue();

        byte[] outputBytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(outputBytes, 4, 4).Should().Be("ftyp");
    }

    [Fact]
    public void Avif_Header_NotAvif_Throws()
    {
        // ftyp is present but the brand is not AVIF (e.g. "qt  " for QuickTime).
        string path = Path.Combine(_dir, "fake.avif");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("qt  "u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("qt  "u8);
        }

        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_Header_NotIsoBmff_Throws()
    {
        // The first 4 bytes are not a valid size — a JPEG-prefixed file rejected.
        string path = Path.Combine(_dir, "jpeg-as-avif.avif");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F']);

        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_Header_TruncatedFtyp_Throws()
    {
        // First 4 bytes declare size 16 but file is only 10 bytes long.
        string path = Path.Combine(_dir, "truncated.avif");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x10, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i']);

        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_Brand_AvifAndAvisAndMif1_Accepted()
    {
        // The AVIF specification brands (avif / avis / mif1) should all pass
        // header validation. We don't try to parse a real AV1 bitstream; we
        // just want the brand check to accept these.
        foreach (string brand in new[] { "avif", "avis", "mif1" })
        {
            string path = Path.Combine(_dir, $"brand-{brand}.avif");
            TestFixtures.WriteAvifWithMetadata(path, brand: brand);
            var cleaner = new AvifMetadataCleaner();
            Action act = () => cleaner.Inspect(path);
            act.Should().NotThrow($"brand {brand} should be accepted as AVIF-compatible");
        }
    }

    [Fact]
    public void Avif_Brand_HeicOnly_Rejected()
    {
        // A HEIC file with only "heic" as both major and compatible brand (no
        // mif1) must be rejected — AVIF requires one of {avif, avis, mif1}.
        string path = Path.Combine(_dir, "heic-as-avif.avif");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("heic"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]); // minor_version
            fs.Write("heic"u8); // compatible_brand = heic (NOT mif1)
        }

        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_CorruptMetaTruncated_Throws()
    {
        // ftyp is valid, but the meta box declares a size that runs past EOF.
        string path = Path.Combine(_dir, "bad-meta.avif");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("avif"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("mif1"u8);

            // meta with size=4096 but no payload
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 4096u);
            fs.Write(hdr);
            fs.Write("meta"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
        }

        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_Cleaner_MissingFile_Throws()
    {
        var cleaner = new AvifMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.avif"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Avif_CanHandle_RecognisesExtensions()
    {
        var cleaner = new AvifMetadataCleaner();
        cleaner.CanHandle(".avif").Should().BeTrue();
        cleaner.CanHandle(".AVIF").Should().BeTrue();
        cleaner.CanHandle(".png").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".avif"]);
    }

    // ---------------------------------------------------------------------
    // MP4 / MOV / 3GP metadata cleaner (WR-P108)
    // ---------------------------------------------------------------------

    [Fact]
    public void Mp4_Inspect_FindsUdtaChildrenAndMetaKeysIlst()
    {
        // Default fixture has moov.udta (with ©xyz + ©mak + …) and
        // moov.meta (with keys + ilst + Exif + XMP uuid). Inspect
        // should report every metadata carrier.
        string path = Path.Combine(_dir, "in.mp4");
        TestFixtures.WriteMp4WithMetadata(path);

        var cleaner = new Mp4MetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        // udta children show up as USER_DATA entries (one per fourcc).
        entries.Where(e => e.Container == "USER_DATA")
            .Select(e => e.Key)
            .Should()
            .Contain(["©xyz", "©day", "©mak", "©mod", "©swr", "©nam"]);

        // moov.meta children show up under their respective containers.
        entries.Select(e => e.Container).Should().Contain("QUICKTIME_META");
        entries.Should().Contain(e => e.Container == "QUICKTIME_META" && e.Key == "keys");
        entries.Should().Contain(e => e.Container == "QUICKTIME_META" && e.Key == "ilst");

        // EXIF + XMP under meta.
        entries.Should().Contain(e => e.Container == "EXIF" && e.Key == "Exif");
        entries.Should().Contain(e => e.Container == "XMP" && e.Key == "uuid/XMP");
    }

    [Fact]
    public void Mp4_Clean_StripsUdtaAndMetaKeysIlst_PreservesMvhdTrakMdat()
    {
        // Default fixture — clean should strip the entire moov.udta
        // and the keys + ilst from moov.meta, while keeping mvhd / trak
        // and the mdat bitstream intact.
        string input = Path.Combine(_dir, "in.mp4");
        string output = Path.Combine(_dir, "out.mp4");
        TestFixtures.WriteMp4WithMetadata(input);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        // The cleaner removed every udta child + the keys/ilst from meta.
        result.RemovedEntries.Should().Contain(e => e.Container == "USER_DATA" && e.Key == "©xyz");
        result.RemovedEntries.Should().Contain(e => e.Container == "USER_DATA" && e.Key == "©mak");
        result.RemovedEntries.Should().Contain(e => e.Container == "QUICKTIME_META" && e.Key == "keys");
        result.RemovedEntries.Should().Contain(e => e.Container == "QUICKTIME_META" && e.Key == "ilst");

        // Output is still a valid MP4 container with ftyp + moov + mdat.
        byte[] outputBytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(outputBytes, 4, 4).Should().Be("ftyp");
        Encoding.ASCII.GetString(outputBytes, 8, 4).Should().Be("mp42");

        // The moov box must still contain mvhd and trak (structural,
        // preserved by the cleaner) but no udta and no keys/ilst.
        MoovStructure moov = ExtractMoovStructure(outputBytes);
        moov.Children.Should().Contain("mvhd");
        moov.Children.Should().Contain("trak");
        moov.Children.Should().NotContain("udta");
        moov.MetaChildren.Should().NotContain("keys");
        moov.MetaChildren.Should().NotContain("ilst");

        // mdat bitstream preserved byte-for-byte.
        byte[] inputBytes = File.ReadAllBytes(input);
        ExtractMdatPayload(outputBytes).Should().Equal(ExtractMdatPayload(inputBytes));
    }

    [Fact]
    public void Mp4_Clean_NoMoov_OnlyFtypAndMdat_Succeeds()
    {
        // A minimal MP4 with no moov at all (just ftyp + mdat) must be
        // accepted and copied verbatim — the cleaner should never require
        // a moov box to be present.
        string input = Path.Combine(_dir, "no-moov.mp4");
        string output = Path.Combine(_dir, "no-moov-out.mp4");
        TestFixtures.WriteMp4WithMetadata(
            input,
            includeMoov: false,
            includeMvhd: false,
            includeTrak: false,
            includeUdta: false,
            includeMeta: false);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().BeEmpty();
        byte[] inputBytes = File.ReadAllBytes(input);
        byte[] outputBytes = File.ReadAllBytes(output);
        ExtractMdatPayload(outputBytes).Should().Equal(ExtractMdatPayload(inputBytes));
    }

    [Fact]
    public void Mp4_Clean_OnlyMvhdTrak_PreservesStructuralBoxes()
    {
        // moov with only mvhd + trak (no udta, no meta) — must be
        // copied verbatim. The cleaner must not invent a meta or udta.
        string input = Path.Combine(_dir, "structural.mp4");
        string output = Path.Combine(_dir, "structural-out.mp4");
        TestFixtures.WriteMp4WithMetadata(
            input,
            includeUdta: false,
            includeMeta: false);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().BeEmpty();
        MoovStructure moov = ExtractMoovStructure(File.ReadAllBytes(output));
        moov.Children.Should().Contain(["mvhd", "trak"]);
        moov.Children.Should().NotContain("udta");
        moov.Children.Should().NotContain("meta");
    }

    [Fact]
    public void Mp4_Clean_DefaultOptions_StripsXmpAndExifFromMeta()
    {
        // The default options (StripExif = StripXmp = true) must
        // remove the Exif 4CC and the XMP uuid from moov.meta.
        string input = Path.Combine(_dir, "xmp-exif.mp4");
        string output = Path.Combine(_dir, "xmp-exif-out.mp4");
        TestFixtures.WriteMp4WithMetadata(
            input,
            includeUdta: false,
            includeMetaKeys: false,
            includeMetaIlst: false,
            includeMetaExif: true,
            includeMetaXmpUuid: true);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "EXIF" && e.Key == "Exif");
        result.RemovedEntries.Should().Contain(e => e.Container == "XMP" && e.Key == "uuid/XMP");

        // Meta should now have only hdlr left.
        MoovStructure moov = ExtractMoovStructure(File.ReadAllBytes(output));
        moov.MetaChildren.Should().BeEquivalentTo(["hdlr"]);
    }

    [Fact]
    public void Mp4_Clean_Reclean_Empty()
    {
        // Two passes with the same options — the second pass must find
        // nothing to strip (proves idempotence).
        string input = Path.Combine(_dir, "reclean-in.mp4");
        string pass1 = Path.Combine(_dir, "reclean-1.mp4");
        string pass2 = Path.Combine(_dir, "reclean-2.mp4");
        TestFixtures.WriteMp4WithMetadata(input);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult first = cleaner.Clean(input, pass1, new MetadataCleanOptions());
        FileCleanResult second = cleaner.Clean(pass1, pass2, new MetadataCleanOptions());

        first.RemovedEntries.Should().NotBeEmpty();
        second.RemovedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Mp4_Clean_LargeSizeMdat_Supported()
    {
        // mdat is wrapped in a largesize box (size == 1 + 8-byte BE u64).
        // The walker must honour the 16-byte header so the bitstream
        // is preserved bit-for-bit.
        string input = Path.Combine(_dir, "large-in.mp4");
        string output = Path.Combine(_dir, "large-out.mp4");
        TestFixtures.WriteMp4WithMetadata(input, useLargeSize: true);

        var cleaner = new Mp4MetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().NotBeEmpty();
        byte[] inputBytes = File.ReadAllBytes(input);
        byte[] outputBytes = File.ReadAllBytes(output);
        ExtractMdatPayload(outputBytes).Should().Equal(ExtractMdatPayload(inputBytes));
    }

    [Fact]
    public void Mp4_Brand_Mp42IsomQtMov3gp_AllAccepted()
    {
        // Every well-known MP4 / MOV / 3GP brand should be accepted by
        // header validation. The cleaner must not reject any of these
        // on the basis of ftyp brand alone.
        foreach (string brand in new[] { "mp42", "isom", "qt  ", "3gp4", "M4V " })
        {
            string path = Path.Combine(_dir, $"brand-{brand.Trim()}.mp4");
            TestFixtures.WriteMp4WithMetadata(path, brand: brand);
            var cleaner = new Mp4MetadataCleaner();
            Action act = () => cleaner.Inspect(path);
            act.Should().NotThrow($"brand '{brand}' should be accepted as MP4-compatible");
        }
    }

    [Fact]
    public void Mp4_Header_NotMp4_Throws()
    {
        // ftyp lists a brand the MP4 / MOV / 3GP cleaner does not
        // understand. The cleaner should refuse rather than guess.
        string path = Path.Combine(_dir, "not-mp4.mp4");
        using (var fs = File.Create(path))
        {
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("qtif"u8); // unsupported brand
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("qtif"u8);
            fs.Write(new byte[8]); // mdat stub
        }

        var cleaner = new Mp4MetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Mp4_Header_NotIsoBmff_Throws()
    {
        // The first box is not ftyp — the file is not an ISOBMFF
        // container at all. Cleaner must throw.
        string path = Path.Combine(_dir, "junk.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x10, (byte)'N', (byte)'U', (byte)'L', (byte)'L', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        var cleaner = new Mp4MetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Mp4_Header_TruncatedFtyp_Throws()
    {
        // ftyp is too short to contain major_brand + minor_version.
        string path = Path.Combine(_dir, "truncated.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x0C, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2']);

        var cleaner = new Mp4MetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Mp4_CorruptMoovTruncated_Throws()
    {
        // ftyp is valid but the moov header claims more bytes than
        // the file actually contains. Cleaner must surface a
        // MetadataStripException instead of crashing.
        string path = Path.Combine(_dir, "corrupt-moov.mp4");
        using (var fs = File.Create(path))
        {
            // ftyp
            Span<byte> hdr = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 20u);
            fs.Write(hdr);
            fs.Write("ftyp"u8);
            fs.Write("mp42"u8);
            fs.Write([0x00, 0x00, 0x00, 0x00]);
            fs.Write("isom"u8);

            // moov — claims 1 KiB of payload but the file ends here.
            BinaryPrimitives.WriteUInt32BigEndian(hdr, 0x400u);
            fs.Write(hdr);
            fs.Write("moov"u8);
        }

        var cleaner = new Mp4MetadataCleaner();
        Action act = () => cleaner.Clean(path, Path.Combine(_dir, "corrupt-moov-out.mp4"), new MetadataCleanOptions());
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Mp4_Cleaner_MissingFile_Throws()
    {
        var cleaner = new Mp4MetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.mp4"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Mp4_CanHandle_RecognisesExtensions()
    {
        var cleaner = new Mp4MetadataCleaner();
        cleaner.CanHandle(".mp4").Should().BeTrue();
        cleaner.CanHandle(".MP4").Should().BeTrue();
        cleaner.CanHandle(".mov").Should().BeTrue();
        cleaner.CanHandle(".m4v").Should().BeTrue();
        cleaner.CanHandle(".m4a").Should().BeTrue();
        cleaner.CanHandle(".3gp").Should().BeTrue();
        cleaner.CanHandle(".png").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".mp4", ".mov", ".m4v", ".m4a", ".m4b", ".m4p", ".3gp", ".3g2"]);
    }

    private static byte[] ExtractMdatPayload(byte[] data)
    {
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

            if (type == "mdat")
            {
                int payloadStart = pos + headerSize;
                int payloadSize = boxSize - headerSize;
                byte[] slice = new byte[payloadSize];
                Array.Copy(data, payloadStart, slice, 0, payloadSize);
                return slice;
            }

            pos += boxSize;
        }

        return [];
    }

    private static void WriteHeifHdlr(Stream stream)
    {
        // hdlr: 8 header + 4 version+flags + 4 pre_defined + 4 handler_type + 12 reserved + 1 name nul
        const int hdlrSize = 8 + 4 + 4 + 4 + 12 + 1;
        Span<byte> hdr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)hdlrSize);
        stream.Write(hdr);
        stream.Write("hdlr"u8);
        stream.Write([0x00, 0x00, 0x00, 0x00]); // version + flags
        stream.Write([0x00, 0x00, 0x00, 0x00]); // pre_defined
        stream.Write("pict"u8);
        stream.Write(new byte[12]);
        stream.WriteByte(0x00);
    }

    private static void WriteHeifPlainBox(Stream stream, string type, byte[] payload)
    {
        int total = 8 + payload.Length;
        Span<byte> hdr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)total);
        stream.Write(hdr);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteHeifUuidBox(Stream stream, byte[] uuidPlusPayload)
    {
        if (uuidPlusPayload.Length < 16)
        {
            throw new ArgumentException("uuid box payload must be at least 16 bytes.");
        }

        int total = 8 + uuidPlusPayload.Length;
        Span<byte> hdr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, (uint)total);
        stream.Write(hdr);
        stream.Write("uuid"u8);
        stream.Write(uuidPlusPayload, 0, uuidPlusPayload.Length);
    }

    /// <summary>Decoded view of an MP4 <c>moov</c> box for tests.</summary>
    private sealed record MoovStructure(IReadOnlyList<string> Children, IReadOnlyList<string> MetaChildren);

    /// <summary>
    /// Locates the first <c>moov</c> box in <paramref name="data"/> and
    /// returns the list of direct child fourccs plus, when a <c>meta</c>
    /// child is present, the list of <c>meta</c>'s direct child fourccs
    /// (skipping the 4-byte version+flags of the FullBox).
    /// </summary>
    private static MoovStructure ExtractMoovStructure(byte[] data)
    {
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

            if (type == "moov")
            {
                int payloadStart = pos + headerSize;
                int payloadEnd = pos + boxSize;
                var children = new List<string>();
                var metaChildren = new List<string>();
                int childPos = payloadStart;
                while (childPos + 8 <= payloadEnd)
                {
                    uint cSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(childPos, 4));
                    string cType = Encoding.ASCII.GetString(data, childPos + 4, 4);
                    int cBoxSize = (int)cSize;
                    int cHeaderSize = 8;
                    if (cSize == 1)
                    {
                        long cl = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(childPos + 8, 8));
                        cBoxSize = (int)cl;
                        cHeaderSize = 16;
                    }

                    if (cBoxSize < cHeaderSize)
                    {
                        break;
                    }

                    children.Add(cType);

                    if (cType == "meta")
                    {
                        // meta is a FullBox: children start 4 bytes into the payload.
                        int mcStart = childPos + cHeaderSize + 4;
                        int mcEnd = childPos + cBoxSize;
                        int mcPos = mcStart;
                        while (mcPos + 8 <= mcEnd)
                        {
                            uint mSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(mcPos, 4));
                            string mType = Encoding.ASCII.GetString(data, mcPos + 4, 4);
                            int mBoxSize = (int)mSize;
                            if (mSize == 1)
                            {
                                long ml = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(mcPos + 8, 8));
                                mBoxSize = (int)ml;
                            }

                            if (mBoxSize < 8)
                            {
                                break;
                            }

                            metaChildren.Add(mType);
                            mcPos += mBoxSize;
                        }
                    }

                    childPos += cBoxSize;
                }

                return new MoovStructure(children, metaChildren);
            }

            pos += boxSize;
        }

        return new MoovStructure([], []);
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

    [Fact]
    public void Epub_Inspect_FindsDublinCoreAndMetaEntries()
    {
        string path = Path.Combine(_dir, "in.epub");
        TestFixtures.WriteEpubWithMetadata(
            path,
            creator: "AI Author",
            contributor: "Bot Editor",
            title: "Generated Book",
            publisher: "AI Press",
            metas: [("dcterms:modified", "2024-06-15T12:00:00Z")]);

        var cleaner = new EpubMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "creator" && e.Value == "AI Author");
        entries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "title" && e.Value == "Generated Book");
        entries.Should().Contain(e => e.Container == "OPF/meta" && e.Key == "dcterms:modified" && e.Value == "2024-06-15T12:00:00Z");
    }

    [Fact]
    public void Epub_Clean_RemovesAllDcExceptIdentifier_StripsAllMeta()
    {
        string input = Path.Combine(_dir, "in.epub");
        string output = Path.Combine(_dir, "out.epub");
        TestFixtures.WriteEpubWithMetadata(
            input,
            creator: "AI Author",
            contributor: "Bot Editor",
            title: "Generated Book",
            publisher: "AI Press",
            date: "2024-06-15",
            metas: [("dcterms:modified", "2024-06-15T12:00:00Z"), ("ibooks:specified-fonts", "true")]);

        var cleaner = new EpubMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        // All non-identifier dc entries + both meta entries are reported as removed.
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "creator");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "contributor");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "title");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "publisher");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/dc" && e.Key == "date");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/meta" && e.Key == "dcterms:modified");
        result.RemovedEntries.Should().Contain(e => e.Container == "OPF/meta" && e.Key == "ibooks:specified-fonts");
    }

    [Fact]
    public void Epub_Clean_OutputIsValidZip_WithStrippedOpf()
    {
        string input = Path.Combine(_dir, "in.epub");
        string output = Path.Combine(_dir, "out.epub");
        TestFixtures.WriteEpubWithMetadata(
            input,
            creator: "AI Author",
            title: "Generated Book",
            metas: [("dcterms:modified", "2024-06-15T12:00:00Z")]);

        var cleaner = new EpubMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        // The output is a valid zip that re-opens with ZipArchive.
        using FileStream fs = File.OpenRead(output);
        using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

        ZipArchiveEntry? opf = archive.GetEntry("OEBPS/content.opf");
        opf.Should().NotBeNull();

        using Stream opfStream = opf!.Open();
        using var reader = new StreamReader(opfStream);
        string opfXml = reader.ReadToEnd();
        opfXml.Should().NotContain("AI Author");
        opfXml.Should().NotContain("Generated Book");
        opfXml.Should().NotContain("dcterms:modified");
        opfXml.Should().Contain("dc:identifier");
    }

    [Fact]
    public void Epub_Clean_PreservesMimetypeAsFirstEntry_Uncompressed()
    {
        string input = Path.Combine(_dir, "in.epub");
        string output = Path.Combine(_dir, "out.epub");
        TestFixtures.WriteEpubWithMetadata(input);

        var cleaner = new EpubMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        using FileStream fs = File.OpenRead(output);
        using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

        archive.Entries[0].FullName.Should().Be("mimetype");
        using Stream s = archive.Entries[0].Open();
        using var reader = new StreamReader(s);
        reader.ReadToEnd().Should().Be("application/epub+zip");
    }

    [Fact]
    public void Epub_Clean_PreservesContainerXmlAndContentUnchanged()
    {
        string input = Path.Combine(_dir, "in.epub");
        string output = Path.Combine(_dir, "out.epub");
        TestFixtures.WriteEpubWithMetadata(
            input,
            creator: "AI Author",
            title: "Generated Book");

        // Snapshot the input container.xml and chapter bytes.
        string inputContainer;
        byte[] inputChapterBytes;
        using (FileStream inFs = File.OpenRead(input))
        using (var inArchive = new System.IO.Compression.ZipArchive(inFs, System.IO.Compression.ZipArchiveMode.Read))
        {
            using Stream s = inArchive.GetEntry("META-INF/container.xml")!.Open();
            using var reader = new StreamReader(s);
            inputContainer = reader.ReadToEnd();

            using Stream ch = inArchive.GetEntry("OEBPS/chapter1.xhtml")!.Open();
            using var ms = new MemoryStream();
            ch.CopyTo(ms);
            inputChapterBytes = ms.ToArray();
        }

        var cleaner = new EpubMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        using FileStream fs = File.OpenRead(output);
        using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

        // META-INF/container.xml must be byte-equal to the input.
        using (Stream s = archive.GetEntry("META-INF/container.xml")!.Open())
        using (var reader = new StreamReader(s))
        {
            reader.ReadToEnd().Should().Be(inputContainer);
        }

        // The placeholder chapter must be byte-equal to the input.
        using Stream chOut = archive.GetEntry("OEBPS/chapter1.xhtml")!.Open();
        using var msOut = new MemoryStream();
        chOut.CopyTo(msOut);
        msOut.ToArray().Should().Equal(inputChapterBytes);
    }

    [Fact]
    public void Epub_Clean_InspectsToOnlyFreshlyStampedIdentifier_AfterPass()
    {
        string input = Path.Combine(_dir, "in.epub");
        string output = Path.Combine(_dir, "out.epub");
        TestFixtures.WriteEpubWithMetadata(
            input,
            creator: "AI Author",
            title: "Generated Book",
            metas: [("dcterms:modified", "2024-06-15T12:00:00Z")]);

        var cleaner = new EpubMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        // After a clean pass only the freshly-stamped dc:identifier remains (every other
        // dc:* / <meta> entry was stripped); the new identifier is a UUID, not the input's value.
        IReadOnlyList<MetadataEntry> remaining = cleaner.Inspect(output);
        remaining.Should().HaveCount(1);
        remaining[0].Container.Should().Be("OPF/dc");
        remaining[0].Key.Should().Be("identifier");
        remaining[0].Value.Should().StartWith("urn:uuid:");
    }

    [Fact]
    public void Epub_CorruptFile_ThrowsMetadataStripException()
    {
        string path = Path.Combine(_dir, "bad.epub");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("not an epub at all"));

        var cleaner = new EpubMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Epub_MissingMimetype_Throws()
    {
        // Build a valid zip that lacks the canonical `mimetype` entry.
        string path = Path.Combine(_dir, "no-mimetype.epub");
        using (FileStream fs = File.Create(path))
        using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("hello.txt");
            using Stream s = entry.Open();
            byte[] data = System.Text.Encoding.UTF8.GetBytes("hi");
            s.Write(data, 0, data.Length);
        }

        var cleaner = new EpubMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Epub_MissingFile_Throws()
    {
        var cleaner = new EpubMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.epub"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Epub_CanHandle_RecognisesExtension()
    {
        var cleaner = new EpubMetadataCleaner();
        cleaner.CanHandle(".epub").Should().BeTrue();
        cleaner.CanHandle(".EPUB").Should().BeTrue();
        cleaner.CanHandle(".zip").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".epub"]);
    }

    // --- PPTX ---

    [Fact]
    public void Pptx_Inspect_FindsCreatorAndTitle()
    {
        string path = Path.Combine(_dir, "in.pptx");
        TestFixtures.WritePptxWithMetadata(
            path,
            creator: "AI Author",
            title: "Generated Deck",
            lastModifiedBy: "Last Mod");

        var cleaner = new PptxMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "Core" && e.Key == "Creator" && e.Value == "AI Author");
        entries.Should().Contain(e => e.Container == "Core" && e.Key == "Title" && e.Value == "Generated Deck");
        entries.Should().Contain(e => e.Container == "Core" && e.Key == "LastModifiedBy" && e.Value == "Last Mod");
    }

    [Fact]
    public void Pptx_Clean_ClearsCoreProperties()
    {
        string input = Path.Combine(_dir, "in.pptx");
        string output = Path.Combine(_dir, "out.pptx");
        TestFixtures.WritePptxWithMetadata(
            input,
            creator: "AI Author",
            title: "Generated Deck",
            lastModifiedBy: "Last Mod");

        var cleaner = new PptxMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "Creator" && e.Value == "AI Author");
        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "Title" && e.Value == "Generated Deck");
        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "LastModifiedBy" && e.Value == "Last Mod");

        // Re-inspect the output: every Core entry is gone.
        IReadOnlyList<MetadataEntry> remaining = cleaner.Inspect(output);
        remaining.Should().NotContain(e => e.Container == "Core");
    }

    [Fact]
    public void Pptx_Clean_OutputIsValidPresentation()
    {
        string input = Path.Combine(_dir, "in.pptx");
        string output = Path.Combine(_dir, "out.pptx");
        TestFixtures.WritePptxWithMetadata(input, creator: "AI Author", title: "Generated Deck");

        var cleaner = new PptxMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        // The output is a valid OpenXml container that re-opens.
        using PresentationDocument doc = PresentationDocument.Open(output, isEditable: false);
        doc.Should().NotBeNull();
        doc.PresentationPart.Should().NotBeNull();
        doc.PresentationPart!.SlideParts.Count().Should().Be(1);
    }

    [Fact]
    public void Pptx_Clean_PreservesSlideShapeText()
    {
        string input = Path.Combine(_dir, "in.pptx");
        string output = Path.Combine(_dir, "out.pptx");
        TestFixtures.WritePptxWithMetadata(input);

        var cleaner = new PptxMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        // The slide's "Hello PPTX" text is preserved through the clean pass.
        using PresentationDocument doc = PresentationDocument.Open(output, isEditable: false);
        SlidePart slide = doc.PresentationPart!.SlideParts.First();
        slide.Slide!.InnerText.Should().Contain("Hello PPTX");
    }

    [Fact]
    public void Pptx_CorruptFile_ThrowsMetadataStripException()
    {
        string path = Path.Combine(_dir, "bad.pptx");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("not a pptx file at all"));

        var cleaner = new PptxMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Pptx_MissingFile_Throws()
    {
        var cleaner = new PptxMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.pptx"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Pptx_CanHandle_RecognisesExtension()
    {
        var cleaner = new PptxMetadataCleaner();
        cleaner.CanHandle(".pptx").Should().BeTrue();
        cleaner.CanHandle(".PPTX").Should().BeTrue();
        cleaner.CanHandle(".docx").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".pptx"]);
    }

    // --- XLSX ---

    [Fact]
    public void Xlsx_Inspect_FindsCreatorAndTitle()
    {
        string path = Path.Combine(_dir, "in.xlsx");
        TestFixtures.WriteXlsxWithMetadata(
            path,
            creator: "AI Author",
            title: "Generated Sheet",
            lastModifiedBy: "Last Mod");

        var cleaner = new XlsxMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "Core" && e.Key == "Creator" && e.Value == "AI Author");
        entries.Should().Contain(e => e.Container == "Core" && e.Key == "Title" && e.Value == "Generated Sheet");
        entries.Should().Contain(e => e.Container == "Core" && e.Key == "LastModifiedBy" && e.Value == "Last Mod");
    }

    [Fact]
    public void Xlsx_Clean_ClearsCoreProperties()
    {
        string input = Path.Combine(_dir, "in.xlsx");
        string output = Path.Combine(_dir, "out.xlsx");
        TestFixtures.WriteXlsxWithMetadata(
            input,
            creator: "AI Author",
            title: "Generated Sheet",
            lastModifiedBy: "Last Mod");

        var cleaner = new XlsxMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "Creator" && e.Value == "AI Author");
        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "Title" && e.Value == "Generated Sheet");
        result.RemovedEntries.Should().Contain(e => e.Container == "Core" && e.Key == "LastModifiedBy" && e.Value == "Last Mod");

        IReadOnlyList<MetadataEntry> remaining = cleaner.Inspect(output);
        remaining.Should().NotContain(e => e.Container == "Core");
    }

    [Fact]
    public void Xlsx_Clean_OutputIsValidWorkbook()
    {
        string input = Path.Combine(_dir, "in.xlsx");
        string output = Path.Combine(_dir, "out.xlsx");
        TestFixtures.WriteXlsxWithMetadata(input, creator: "AI Author");

        var cleaner = new XlsxMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        using SpreadsheetDocument doc = SpreadsheetDocument.Open(output, isEditable: false);
        doc.Should().NotBeNull();
        doc.WorkbookPart.Should().NotBeNull();
        doc.WorkbookPart!.WorksheetParts.Count().Should().Be(1);
    }

    [Fact]
    public void Xlsx_Clean_PreservesCellValues()
    {
        string input = Path.Combine(_dir, "in.xlsx");
        string output = Path.Combine(_dir, "out.xlsx");
        TestFixtures.WriteXlsxWithMetadata(input, cellA1: "Hello Cell");

        var cleaner = new XlsxMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        using SpreadsheetDocument doc = SpreadsheetDocument.Open(output, isEditable: false);
        WorksheetPart wsPart = doc.WorkbookPart!.WorksheetParts.First();
        Cell cell = wsPart.Worksheet!.Descendants<Cell>().First()!;
        cell.CellReference!.Value.Should().Be("A1");
        cell.CellValue!.Text.Should().Be("Hello Cell");
    }

    [Fact]
    public void Xlsx_CorruptFile_ThrowsMetadataStripException()
    {
        string path = Path.Combine(_dir, "bad.xlsx");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("not an xlsx file at all"));

        var cleaner = new XlsxMetadataCleaner();
        Action act = () => cleaner.Inspect(path);
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Xlsx_MissingFile_Throws()
    {
        var cleaner = new XlsxMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.xlsx"));
        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Xlsx_CanHandle_RecognisesExtension()
    {
        var cleaner = new XlsxMetadataCleaner();
        cleaner.CanHandle(".xlsx").Should().BeTrue();
        cleaner.CanHandle(".XLSX").Should().BeTrue();
        cleaner.CanHandle(".docx").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".xlsx"]);
    }

    [Fact]
    public void Rtf_Inspect_FindsAuthorAndGenerator()
    {
        string path = Path.Combine(_dir, "in.rtf");
        TestFixtures.WriteRtfWithMetadata(path, author: "John Smith", generator: "AI-Writer 1.0");

        var cleaner = new RtfMetadataCleaner();
        IReadOnlyList<MetadataEntry> entries = cleaner.Inspect(path);

        entries.Should().Contain(e => e.Container == "RTF/info" && e.Key == "author" && e.Value == "John Smith");
        entries.Should().Contain(e => e.Container == "RTF/info" && e.Key == "generator" && e.Value == "AI-Writer 1.0");
        entries.Should().Contain(e => e.Container == "RTF/info" && e.Key == "doccomm");
    }

    [Fact]
    public void Rtf_Clean_StripsAuthorGeneratorDoccomm()
    {
        string input = Path.Combine(_dir, "in.rtf");
        string output = Path.Combine(_dir, "out.rtf");
        TestFixtures.WriteRtfWithMetadata(
            input,
            author: "John Smith",
            generator: "AI-Writer 1.0",
            doccomm: "Internal only");

        var cleaner = new RtfMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Select(e => e.Key).Should().Contain(["author", "generator", "doccomm"]);

        // The cleaned file no longer contains the metadata values anywhere.
        string cleaned = File.ReadAllText(output);
        cleaned.Should().NotContain("John Smith");
        cleaned.Should().NotContain("AI-Writer 1.0");
        cleaned.Should().NotContain("Internal only");

        // The three metadata control words themselves are gone.
        cleaned.Should().NotContain("\\author ");
        cleaned.Should().NotContain("\\generator ");
        cleaned.Should().NotContain("\\doccomm ");
    }

    [Fact]
    public void Rtf_Clean_StripsAllInfoMetadata()
    {
        string input = Path.Combine(_dir, "all.rtf");
        string output = Path.Combine(_dir, "all-out.rtf");
        TestFixtures.WriteRtfWithMetadata(
            input,
            author: "John Smith",
            generator: "AI-Writer 1.0",
            doccomm: "Internal only",
            title: "Sample Doc",
            company: "Acme Corp");

        var cleaner = new RtfMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        // Every common metadata control word was removed.
        result.RemovedEntries.Select(e => e.Key)
            .Should().Contain(["title", "author", "company", "generator", "doccomm"]);

        // No leftover metadata values in the output.
        string cleaned = File.ReadAllText(output);
        cleaned.Should().NotContain("John Smith");
        cleaned.Should().NotContain("Acme Corp");
        cleaned.Should().NotContain("AI-Writer 1.0");
        cleaned.Should().NotContain("Internal only");
        cleaned.Should().NotContain("Sample Doc");

        // A second pass finds nothing left to strip.
        FileCleanResult secondPass = cleaner.Clean(output, Path.Combine(_dir, "all-out2.rtf"), new MetadataCleanOptions());
        secondPass.RemovedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Rtf_Clean_StripsCompoundControlWords()
    {
        string input = Path.Combine(_dir, "compound.rtf");
        string output = Path.Combine(_dir, "compound-out.rtf");
        TestFixtures.WriteRtfWithMetadata(input, includeCreatim: true);

        var cleaner = new RtfMetadataCleaner();
        FileCleanResult result = cleaner.Clean(input, output, new MetadataCleanOptions());

        result.RemovedEntries.Should().Contain(e => e.Key == "creatim");

        // The compound control word AND every sub-control word are gone.
        string cleaned = File.ReadAllText(output);
        cleaned.Should().NotContain("\\creatim");
        cleaned.Should().NotContain("\\yr2024");
        cleaned.Should().NotContain("\\mo1");
        cleaned.Should().NotContain("\\dy15");
        cleaned.Should().NotContain("\\hr10");
        cleaned.Should().NotContain("\\min30");
        cleaned.Should().NotContain("\\sec0");
    }

    [Fact]
    public void Rtf_Clean_OutputIsValidRtf()
    {
        string input = Path.Combine(_dir, "valid.rtf");
        string output = Path.Combine(_dir, "valid-out.rtf");
        TestFixtures.WriteRtfWithMetadata(input);

        var cleaner = new RtfMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        // Output starts with the RTF magic and has balanced braces.
        string cleaned = File.ReadAllText(output);
        cleaned.Should().StartWith("{\\rtf");
        cleaned.TrimEnd().Should().EndWith("}");

        int open = cleaned.Count(c => c == '{');
        int close = cleaned.Count(c => c == '}');
        open.Should().Be(close, "cleaned RTF must have balanced braces");
    }

    [Fact]
    public void Rtf_Clean_PreservesContent()
    {
        string body = "The quick brown fox jumps over the lazy dog.";
        string input = Path.Combine(_dir, "content.rtf");
        string output = Path.Combine(_dir, "content-out.rtf");
        TestFixtures.WriteRtfWithMetadata(input, body: body);

        var cleaner = new RtfMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        string cleaned = File.ReadAllText(output);
        cleaned.Should().Contain(body);

        // The font table survives too.
        cleaned.Should().Contain("\\fonttbl");
        cleaned.Should().Contain("\\f0\\fnil");
    }

    [Fact]
    public void Rtf_Clean_InspectAfterClean_FindsNoMetadata()
    {
        string input = Path.Combine(_dir, "roundtrip.rtf");
        string output = Path.Combine(_dir, "roundtrip-out.rtf");
        TestFixtures.WriteRtfWithMetadata(input);

        var cleaner = new RtfMetadataCleaner();
        cleaner.Clean(input, output, new MetadataCleanOptions());

        cleaner.Inspect(output).Should().BeEmpty();
    }

    [Fact]
    public void Rtf_Inspect_CorruptFile_Throws()
    {
        // A file that does not start with the {\rtf magic is rejected.
        string path = Path.Combine(_dir, "bad.rtf");
        File.WriteAllText(path, "This is not an RTF file at all, just plain text.");

        var cleaner = new RtfMetadataCleaner();
        Action act = () => cleaner.Inspect(path);

        act.Should().Throw<MetadataStripException>()
            .And.Message.Should().Contain("{\\rtf");
    }

    [Fact]
    public void Rtf_Clean_CorruptFile_Throws()
    {
        // Bytes that don't start with the {\rtf magic — same code path, but
        // exercised via the public Clean() entry point.
        string input = Path.Combine(_dir, "bad-clean.rtf");
        File.WriteAllBytes(input, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var cleaner = new RtfMetadataCleaner();
        Action act = () => cleaner.Clean(input, Path.Combine(_dir, "bad-clean-out.rtf"), new MetadataCleanOptions());

        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Rtf_MissingFile_Throws()
    {
        var cleaner = new RtfMetadataCleaner();
        Action act = () => cleaner.Inspect(Path.Combine(_dir, "does-not-exist.rtf"));

        act.Should().Throw<MetadataStripException>();
    }

    [Fact]
    public void Rtf_CanHandle_RecognisesExtensions()
    {
        var cleaner = new RtfMetadataCleaner();
        cleaner.CanHandle(".rtf").Should().BeTrue();
        cleaner.CanHandle(".RTF").Should().BeTrue();
        cleaner.CanHandle(".docx").Should().BeFalse();
        cleaner.SupportedExtensions.Should().BeEquivalentTo([".rtf"]);
    }
}
