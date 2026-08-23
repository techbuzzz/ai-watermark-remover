using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using SkiaSharp;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Mcp.Tools;
using WatermarkRemover.Metadata;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// Unit tests for <see cref="InspectFileTool"/>. Mirrors the
/// CleanFileTool tests against a real PNG with a tEXt chunk so the
/// full inspect pipeline is exercised.
/// </summary>
public sealed class InspectFileToolTests : IDisposable
{
    private readonly string _tempDir;

    public InspectFileToolTests()
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
    public void InspectFile_PngWithTextChunk_ReturnsNonEmptyArray()
    {
        string path = Path.Combine(_tempDir, "fixture.png");
        WritePngWithTextChunk(path, keyword: "Software", value: "WatermarkRemover Test");

        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        TextContentBlock result = InspectFileTool.InspectFile(router, path);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        JsonElement.ArrayEnumerator arr = doc.RootElement.EnumerateArray();
        List<JsonElement> elements = arr.ToList();
        elements.Should().NotBeEmpty();

        // MetadataEntry record shape: Container / Key / Value.
        JsonElement first = elements[0];
        first.TryGetProperty("Container", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Key", out JsonElement _).Should().BeTrue();
        first.TryGetProperty("Value", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public void InspectFile_UnsupportedExtension_ThrowsMcpException()
    {
        string path = Path.Combine(_tempDir, "fixture.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        Action act = () => InspectFileTool.InspectFile(router, path);

        act.Should().Throw<ModelContextProtocol.McpException>()
            .WithMessage("*Unsupported file type*");
    }

    [Fact]
    public void InspectFile_MissingFile_ThrowsMcpException()
    {
        ServiceProvider sp = BuildMetadataHost();
        IFileCleanerRouter router = sp.GetRequiredService<IFileCleanerRouter>();

        Action act = () => InspectFileTool.InspectFile(router, Path.Combine(_tempDir, "missing.png"));

        act.Should().Throw<ModelContextProtocol.McpException>()
            .WithMessage("*File not found*");
    }

    private static ServiceProvider BuildMetadataHost()
    {
        ServiceCollection services = new();
        services.AddWatermarkRemoverMetadata();
        services.AddSingleton(AppConfig.Default);
        return services.BuildServiceProvider();
    }

    // The next two helpers are duplicated from CleanFileToolTests on
    // purpose — each test fixture is responsible for its own fixtures
    // and the PNG-with-tEXt-chunk writer is small enough that the cost
    // of a shared helper class is higher than the cost of inlining it
    // in both files. If a third test ever needs the same fixture,
    // promote it to a test helper at that point.

    private static void WritePngWithTextChunk(string path, string keyword, string value)
    {
        using var bitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        pixels.Fill(new SKColor(255, 0, 0, 255));

        using var ms = new MemoryStream();
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.AsStream().CopyTo(ms);
        }
        byte[] original = ms.ToArray();

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
        chunk[8 + keywordBytes.Length] = 0;
        valueBytes.CopyTo(chunk.AsSpan(9 + keywordBytes.Length, valueBytes.Length));
        uint crc = Crc32.Compute(chunk.AsSpan(4, 4 + dataLength));
        WriteUInt32BigEndian(chunk.AsSpan(8 + dataLength, 4), crc);

        using FileStream fs = File.Create(path);
        fs.Write(original, 0, 8);
        fs.Write(chunk, 0, chunk.Length);
        fs.Write(original, 8, original.Length - 8);
    }

    private static void WriteUInt32BigEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 24);
        dest[1] = (byte)(value >> 16);
        dest[2] = (byte)(value >> 8);
        dest[3] = (byte)value;
    }

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
