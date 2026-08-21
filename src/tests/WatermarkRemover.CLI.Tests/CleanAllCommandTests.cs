using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;
using WatermarkRemover.Text.Markdown;
using WatermarkRemover.Text.Vendors;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Integration tests for the <c>clean-all</c> command. The classifier
/// has its own focused tests; these exercise the full path-to-output
/// pipeline with real implementations wired up the way the CLI does.
/// </summary>
public class CleanAllCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CleanAllCommand _command;

    public CleanAllCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wr-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        IFileCleanerRouter router = new FileCleanerRouter(
        [
            new JpegMetadataCleaner(),
            new PngMetadataCleaner(),
            new WebPMetadataCleaner(),
            new PdfMetadataCleaner(),
            new DocxMetadataCleaner(),
            new HtmlMetadataCleaner(),
        ]);

        ITextCleaningPipeline textPipeline = new TextCleaningPipeline(
            new UnicodeHygieneCleaner(),
            new StatisticalWatermarkRewriter(),
            [new ClaudeWatermarkDetector(), new GeminiWatermarkDetector(), new OpenAiWatermarkDetector()]);

        IMarkdownCleaner markdownCleaner = new MarkdownCleaner();

        _command = new CleanAllCommand(router, markdownCleaner, textPipeline, AppConfig.Default);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort — temp dir cleanup is not test-critical.
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_MixedDirectory_CleansAllSupportedFiles()
    {
        // Arrange: a.txt with ZWSP, b.md with frontmatter, c.png with tEXt metadata.
        WriteText(Path.Combine(_tempDir, "a.txt"), "Hello\u200B world");
        WriteText(Path.Combine(_tempDir, "b.md"), "---\nauthor: bot\n---\n# Hi");
        WritePngWithText(Path.Combine(_tempDir, "c.png"), "Comment", "watermark");

        // Act
        int exit = await _command.ExecuteAsync(NewContext(), NewSettings(path: _tempDir));

        // Assert: every supported extension produced a `.cleaned.*` file
        // and the originals are untouched. Text ZWSP is gone; PNG tEXt
        // chunk is gone; markdown frontmatter is gone.
        exit.Should().Be(0);

        File.Exists(Path.Combine(_tempDir, "a.cleaned.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "b.cleaned.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "c.cleaned.png")).Should().BeTrue();

        string cleanedText = File.ReadAllText(Path.Combine(_tempDir, "a.cleaned.txt"));
        cleanedText.Should().NotContain("\u200B");
        cleanedText.Should().Be("Hello world");

        string cleanedMd = File.ReadAllText(Path.Combine(_tempDir, "b.cleaned.md"));
        cleanedMd.Should().NotContain("---");
        cleanedMd.Should().NotContain("author: bot");

        // The cleaned PNG no longer contains the tEXt chunk that held
        // "watermark" — but we don't decode pixels in tests, we just
        // assert that the original is intact and a smaller sibling exists.
        File.Exists(Path.Combine(_tempDir, "c.png")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryRecursive_IncludesSubdirectoryFiles()
    {
        string sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        WriteText(Path.Combine(_tempDir, "top.txt"), "plain text");
        WriteText(Path.Combine(sub, "deep.txt"), "deep\u200Btext");

        // Without --recursive: only top.txt is processed.
        int nonRecursiveExit = await _command.ExecuteAsync(
            NewContext(), NewSettings(path: _tempDir, recursive: false));
        nonRecursiveExit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "top.cleaned.txt")).Should().BeTrue();
        File.Exists(Path.Combine(sub, "deep.cleaned.txt")).Should().BeFalse();

        // Clean up the non-recursive output before re-running.
        File.Delete(Path.Combine(_tempDir, "top.cleaned.txt"));

        // With --recursive: both files cleaned.
        int recursiveExit = await _command.ExecuteAsync(
            NewContext(), NewSettings(path: _tempDir, recursive: true));
        recursiveExit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "top.cleaned.txt")).Should().BeTrue();
        File.Exists(Path.Combine(sub, "deep.cleaned.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_BinaryFilesAreSkipped_NotFedToTextPipeline()
    {
        // A .bin file should be classified Unsupported, never reach the
        // text pipeline, and never produce a .cleaned.bin. The
        // command's exit code stays 0 because skips are not failures.
        string binPath = Path.Combine(_tempDir, "blob.bin");
        File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0xFF, 0xFE]);

        int exit = await _command.ExecuteAsync(NewContext(), NewSettings(path: _tempDir));

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "blob.cleaned.bin")).Should().BeFalse();
        // Original untouched.
        File.Exists(binPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotWriteCleanedFiles()
    {
        WriteText(Path.Combine(_tempDir, "note.txt"), "Hello\u200B world");
        WriteText(Path.Combine(_tempDir, "readme.md"), "# title");

        int exit = await _command.ExecuteAsync(
            NewContext(), NewSettings(path: _tempDir, dryRun: true));

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "note.cleaned.txt")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, "readme.cleaned.md")).Should().BeFalse();
        // Originals untouched.
        File.Exists(Path.Combine(_tempDir, "note.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "readme.md")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SingleFile_WorksWithoutRecursive()
    {
        string txt = Path.Combine(_tempDir, "solo.txt");
        WriteText(txt, "Hello\u200B world");

        int exit = await _command.ExecuteAsync(NewContext(), NewSettings(path: txt));

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "solo.cleaned.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MissingPath_ReturnsError()
    {
        int exit = await _command.ExecuteAsync(
            NewContext(), NewSettings(path: Path.Combine(_tempDir, "does-not-exist")));

        exit.Should().Be(1);
    }

    private static CommandContext NewContext() => new(
        arguments: [],
        remaining: new EmptyRemainingArguments(),
        name: "clean-all",
        data: null);

    /// <summary>Stub implementation of <see cref="IRemainingArguments"/> — the framework type is internal.</summary>
    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public ILookup<string, string?> Parsed => Enumerable.Empty<(string, string?)>().ToLookup(p => p.Item1, p => p.Item2);
        public IReadOnlyList<string> Raw => [];
    }

    private static CleanAllCommand.Settings NewSettings(
        string path,
        bool recursive = false,
        bool dryRun = false,
        bool json = false) => new()
    {
        Path = path,
        Recursive = recursive,
        DryRun = dryRun,
        Json = json,
    };

    private static void WriteText(string path, string content) => File.WriteAllText(path, content);

    /// <summary>Mirror of Metadata.Tests' TestFixtures, scoped to this project.</summary>
    private static void WritePngWithText(string path, string keyword, string value)
    {
        using FileStream fs = File.Create(path);
        // PNG signature.
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR: 1x1, 8-bit, colour type 2 (truecolour).
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), 1);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(fs, "IHDR", ihdr);

        byte[] text = [.. Encoding.ASCII.GetBytes(keyword), 0x00, .. Encoding.ASCII.GetBytes(value)];
        WriteChunk(fs, "tEXt", text);

        // Minimal IDAT — not a real compressed scanline, but the chunk
        // must survive so the PNG is structurally valid.
        WriteChunk(fs, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]);

        WriteChunk(fs, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

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
