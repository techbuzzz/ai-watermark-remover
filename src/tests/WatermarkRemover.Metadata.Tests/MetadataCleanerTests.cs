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
}
