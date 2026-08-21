using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Mcp.Tools;
using WatermarkRemover.Metadata;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// Unit tests for <see cref="CleanFileTool"/>. Uses a tiny in-memory
/// fake router so the test never touches the real metadata pipeline or
/// disk-heavy file types.
/// </summary>
public sealed class CleanFileToolTests : IDisposable
{
    private readonly string _tempDir;

    public CleanFileToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wr-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CleanFile_StripsPngMetadata_ReturnsTextAndResourceBlocks()
    {
        // Arrange — write a PNG that actually has a tEXt chunk so the
        // real PngMetadataCleaner can run end-to-end. We test against
        // the real router because the tool's job is to forward to it.
        string inputPath = Path.Combine(_tempDir, "fixture.png");
        WritePngWithTextChunk(inputPath, keyword: "Software", value: "WatermarkRemover Test");

        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        // Act
        IEnumerable<ContentBlock> blocks = await CleanFileTool.CleanFile(router, AppConfig.Default, inputPath);

        // Assert — two blocks: a JSON summary + a base64 resource block.
        ContentBlock[] arr = blocks.ToArray();
        arr.Should().HaveCount(2);
        arr[0].Should().BeOfType<TextContentBlock>();
        arr[1].Should().BeOfType<EmbeddedResourceBlock>();

        // Sidecar JSON reports the input/output paths and a positive
        // number of removed entries.
        TextContentBlock summary = (TextContentBlock)arr[0];
        using JsonDocument doc = JsonDocument.Parse(summary.Text);
        doc.RootElement.GetProperty("inputPath").GetString().Should().Be(inputPath);
        doc.RootElement.GetProperty("outputPath").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("removedEntries").GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("mimeType").GetString().Should().Be("image/png");

        // The resource block wraps the cleaned PNG bytes.
        EmbeddedResourceBlock resource = (EmbeddedResourceBlock)arr[1];
        resource.Resource.Should().BeOfType<BlobResourceContents>();
        BlobResourceContents blob = (BlobResourceContents)resource.Resource!;
        blob.MimeType.Should().Be("image/png");
        blob.DecodedData.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CleanFile_FileMissing_ThrowsMcpException()
    {
        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        Func<Task> act = () => CleanFileTool.CleanFile(router, AppConfig.Default, Path.Combine(_tempDir, "does-not-exist.png"));

        await act.Should().ThrowAsync<ModelContextProtocol.McpException>()
            .WithMessage("*File not found*");
    }

    [Fact]
    public async Task CleanFile_UnsupportedExtension_ThrowsMcpException()
    {
        string path = Path.Combine(_tempDir, "fixture.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        Func<Task> act = () => CleanFileTool.CleanFile(router, AppConfig.Default, path);

        await act.Should().ThrowAsync<ModelContextProtocol.McpException>()
            .WithMessage("*Unsupported file type*");
    }

    private static ServiceProvider BuildMetadataHost()
    {
        ServiceCollection services = new();
        services.AddWatermarkRemoverMetadata();
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }

    private static void WritePngWithTextChunk(string path, string keyword, string value)
    {
        using Image<Rgba32> image = new(8, 8);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(255, 0, 0, 255);
                }
            }
        });

        PngEncoder encoder = new()
        {
            TextCompressionThreshold = 0, // forces the encoder to emit a tEXt chunk
        };

        // SixLabors doesn't have a first-class "tEXt" property; the
        // simplest deterministic way to seed metadata is to write the
        // PNG via ImageSharp, then inject a tEXt chunk by re-encoding
        // through a MemoryStream and prepending the chunk header.
        using MemoryStream ms = new();
        image.Save(ms, encoder);
        byte[] original = ms.ToArray();

        // PNG signature is 8 bytes; chunks follow. Insert a tEXt chunk
        // right after the signature so the PNG reader sees a single
        // recognized metadata entry.
        byte[] keywordBytes = Encoding.ASCII.GetBytes(keyword);
        byte[] valueBytes = Encoding.ASCII.GetBytes(value);
        int dataLength = keywordBytes.Length + 1 + valueBytes.Length;
        byte[] chunk = new byte[12 + dataLength];
        WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)dataLength);
        chunk[4] = (byte)'t';
        chunk[5] = (byte)'E';
        chunk[6] = (byte)'X';
        chunk[7] = (byte)'t';
        keywordBytes.CopyTo(chunk.AsSpan(8, keywordBytes.Length));
        chunk[8 + keywordBytes.Length] = 0; // null separator
        valueBytes.CopyTo(chunk.AsSpan(9 + keywordBytes.Length, valueBytes.Length));
        uint crc = Crc32.Compute(chunk.AsSpan(4, 4 + dataLength));
        WriteUInt32BigEndian(chunk.AsSpan(8 + dataLength, 4), crc);

        using FileStream fs = File.Create(path);
        fs.Write(original, 0, 8); // signature
        fs.Write(chunk, 0, chunk.Length); // tEXt
        fs.Write(original, 8, original.Length - 8); // rest of file
    }

    private static void WriteUInt32BigEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 24);
        dest[1] = (byte)(value >> 16);
        dest[2] = (byte)(value >> 8);
        dest[3] = (byte)value;
    }

    /// <summary>PNG-spec CRC32 — small implementation to avoid pulling in a CRC dependency.</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = MakeTable();

        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] MakeTable()
        {
            uint[] t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                t[n] = c;
            }
            return t;
        }
    }
}
