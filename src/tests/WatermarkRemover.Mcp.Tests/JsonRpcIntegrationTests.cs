using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SkiaSharp;
using WatermarkRemover.Core;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Image;
using WatermarkRemover.Mcp;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// End-to-end coverage of the MCP server's JSON-RPC surface using the
/// SDK's <see cref="StreamClientTransport"/> / <see cref="ModelContextProtocol.Server.StreamServerTransport"/>
/// pair over paired <see cref="Pipe"/>s. This is the official in-process
/// testing pattern recommended by the SDK — no subprocess, no socket,
/// no port binding. It exercises the full handshake, tool discovery,
/// and tool dispatch loop that a real MCP client would, with one big
/// advantage: failures point at a specific tool, parameter, or wire
/// shape, instead of at a hard-to-reproduce subprocess crash.
/// </summary>
/// <remarks>
/// The test fixture wires the same composition root a real host would
/// (<c>AddWatermarkRemoverCore / Text / Metadata / Image / Mcp</c>) but
/// swaps <see cref="IInpaintRunner"/> for a no-ONNX fake so the image
/// tools can run end-to-end without a downloaded model — same pattern
/// used by the rest of the solution's test suite (see
/// <c>WatermarkRemover.Image.Tests.FakeInpaintRunner</c>).
/// </remarks>
public sealed class JsonRpcIntegrationTests : IClassFixture<McpJsonRpcHost>
{
    private readonly McpJsonRpcHost _host;

    public JsonRpcIntegrationTests(McpJsonRpcHost host)
    {
        _host = host;
    }

    // -------------------------------------------------------------- helpers

    /// <summary>
    /// Asserts a successful tool call. Per the MCP spec, a successful
    /// <c>tools/call</c> response has <c>isError</c> either omitted
    /// (null) or false; we accept both, but the field must NOT be
    /// set to true. The actual content checks are done by each test.
    /// </summary>
    private static void AssertSuccess(CallToolResult result)
    {
        if (result.IsError == true)
        {
            string message = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? "(no text content)";
            throw new Xunit.Sdk.XunitException(
                $"Expected tool call to succeed, but server returned IsError=true. Message: {message}");
        }
    }

    // -------------------------------------------------------------- handshake

    [Fact]
    public async Task Initialize_Handshake_ReturnsServerInfo()
    {
        // After McpClient.CreateAsync the server has already run
        // the JSON-RPC `initialize` handshake. ServerInfo is the
        // canonical thing agents read to identify the server.
        Implementation serverInfo = _host.Client.ServerInfo;

        serverInfo.Should().NotBeNull();
        serverInfo.Name.Should().Be(ServerInfo.ServerName);
        serverInfo.Version.Should().Be(ServerInfo.ServerVersion);
    }

    [Fact]
    public async Task Initialize_Handsshake_AdvertisesToolsCapability()
    {
        // The server advertises its capability set during the
        // initialize handshake. The `tools` capability is the
        // gating bit for `tools/list` and `tools/call` — every
        // tool call below assumes the server is willing to
        // dispatch them.
        ServerCapabilities capabilities = _host.Client.ServerCapabilities;

        capabilities.Should().NotBeNull();
        capabilities.Tools.Should().NotBeNull(
            "the server exposes 8 tools via AddWatermarkRemoverMcp → WithToolsFromAssembly");
    }

    // -------------------------------------------------------------- tools/list

    [Fact]
    public async Task ToolsList_Returns8Tools()
    {
        IList<McpClientTool> tools = await _host.Client.ListToolsAsync();

        tools.Should().HaveCount(8);
        HashSet<string> names = tools.Select(t => t.Name).Where(n => n is not null).Select(n => n!).ToHashSet();
        names.Should().BeEquivalentTo(new[]
        {
            "clean_text", "clean_markdown", "clean_file", "clean_image",
            "detect_text", "detect_markdown", "inspect_file", "detect_watermark",
        });
    }

    // -------------------------------------------------------------- clean_text

    [Fact]
    public async Task CleanText_RemovesZwsp()
    {
        // ZWSP (U+200B) is the canonical invisible watermark char
        // the text pipeline strips in Layer A. After clean_text the
        // primary TextContentBlock should be free of it.
        const string input = "Hello\u200BWorld";
        const string expected = "HelloWorld";

        CallToolResult result = await _host.Client.CallToolAsync(
            "clean_text",
            new Dictionary<string, object?> { ["text"] = input });

        AssertSuccess(result);
        TextContentBlock[] textBlocks = result.Content.OfType<TextContentBlock>().ToArray();
        textBlocks.Should().ContainSingle();
        textBlocks[0].Text.Should().Be(expected);
    }

    // -------------------------------------------------------------- clean_markdown

    [Fact]
    public async Task CleanMarkdown_StripsFrontmatter()
    {
        // A standard YAML frontmatter block (--- ... ---) at the
        // top of the document is the canonical AI-tooling artefact
        // the markdown cleaner strips. The cleaned text should
        // still contain the actual heading and body.
        const string input = "---\ntitle: Test\n---\n# Hello\nWorld\n";

        CallToolResult result = await _host.Client.CallToolAsync(
            "clean_markdown",
            new Dictionary<string, object?> { ["markdown"] = input });

        AssertSuccess(result);
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();
        text.Text.Should().NotContain("title: Test");
        text.Text.Should().Contain("Hello");
        text.Text.Should().Contain("World");
    }

    // -------------------------------------------------------------- detect_text

    [Fact]
    public async Task DetectText_FindsVendorWatermark()
    {
        // The Claude vendor detector flags two signatures:
        //   (1) Cyrillic / Greek homoglyphs inside Latin words;
        //   (2) a run of 2+ zero-width code points (steganographic
        //       bit-encoding).
        // We hit both at once with a homoglyph + zero-width run so
        // the test is robust to either path being the first to
        // match in the future. (See ClaudeWatermarkDetector in
        // WatermarkRemover.Text for the exact heuristics.)
        const string input = "H\u0435llo\u200B\u200B\u200BWorld";

        CallToolResult result = await _host.Client.CallToolAsync(
            "detect_text",
            new Dictionary<string, object?> { ["text"] = input });

        AssertSuccess(result);
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();

        using JsonDocument doc = JsonDocument.Parse(text.Text);
        JsonElement root = doc.RootElement;

        // The detector returns an array. For this specific
        // pattern at least one match should be reported; even
        // if heuristics change later, the contract is "returns
        // a JSON array, never throws on benign input."
        root.ValueKind.Should().Be(JsonValueKind.Array);

        // Belt-and-braces: at least one entry should name
        // "Claude" as the vendor. (The pipeline serialises
        // WatermarkMatch via System.Text.Json, so the property
        // name comes through as PascalCase "Vendor".)
        JsonElement.ArrayEnumerator enumerator = root.EnumerateArray();
        HashSet<string> vendors = new();
        while (enumerator.MoveNext())
        {
            string? vendor = enumerator.Current.TryGetProperty("Vendor", out JsonElement v)
                ? v.GetString()
                : null;
            if (vendor is not null)
            {
                vendors.Add(vendor);
            }
        }
        vendors.Should().Contain("Claude");
    }

    // -------------------------------------------------------------- inspect_file

    [Fact]
    public async Task InspectFile_ReturnsMetadataEntries()
    {
        // Build a tiny PNG with a tEXt chunk that the real
        // PngMetadataCleaner / MetadataRouter can parse. The
        // router's Inspect() should surface it as a MetadataEntry
        // with the right container. (Each `MetadataEntry` uses
        // the chunk type as the key — "tEXt" — and the
        // human-friendly container as "XMP/Text". The JSON
        // serialisation is PascalCase because that's the C#
        // record property name.)
        string inputPath = WritePngWithTextChunk("Software", "Test");

        CallToolResult result = await _host.Client.CallToolAsync(
            "inspect_file",
            new Dictionary<string, object?> { ["input_path"] = inputPath });

        AssertSuccess(result);
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();

        using JsonDocument doc = JsonDocument.Parse(text.Text);
        JsonElement root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);

        // Walk the array; the entry from the tEXt chunk has
        // Container = "XMP/Text" and Key = "tEXt". If a future
        // refactor changes that, the test surfaces it clearly.
        bool found = false;
        foreach (JsonElement entry in root.EnumerateArray())
        {
            string? container = entry.TryGetProperty("Container", out JsonElement c) ? c.GetString() : null;
            string? key = entry.TryGetProperty("Key", out JsonElement k) ? k.GetString() : null;
            if (string.Equals(container, "XMP/Text", StringComparison.Ordinal) &&
                string.Equals(key, "tEXt", StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }
        found.Should().BeTrue("the tEXt chunk we wrote should be visible via inspect_file");
    }

    // -------------------------------------------------------------- clean_file

    [Fact]
    public async Task CleanFile_ReturnsBlobResource()
    {
        // clean_file must return BOTH a JSON sidecar (a TextContentBlock
        // summarising input/output paths + number of removed entries)
        // AND an EmbeddedResourceBlock carrying the cleaned file
        // bytes as a base64-encoded blob.
        string inputPath = WritePngWithTextChunk("Software", "Test");

        CallToolResult result = await _host.Client.CallToolAsync(
            "clean_file",
            new Dictionary<string, object?> { ["input_path"] = inputPath });

        AssertSuccess(result);
        result.Content.Should().HaveCount(2);
        result.Content[0].Should().BeOfType<TextContentBlock>();
        result.Content[1].Should().BeOfType<EmbeddedResourceBlock>();

        EmbeddedResourceBlock resource = (EmbeddedResourceBlock)result.Content[1];
        resource.Resource.Should().BeOfType<BlobResourceContents>();
        BlobResourceContents blob = (BlobResourceContents)resource.Resource!;
        blob.MimeType.Should().Be("image/png");
        blob.DecodedData.Length.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------- clean_image

    [Fact]
    public async Task CleanImage_ReturnsImageContentBlock()
    {
        // The image tool always re-encodes to PNG regardless of
        // input format, so a 32x32 PNG fixture is the most
        // straightforward thing to feed it. The fake inpaint runner
        // paints nothing (NoOp mask) so this is a pass-through
        // encode.
        string inputPath = WriteSolidPng(32, 32, new SKColor(255, 0, 0, 255));

        CallToolResult result = await _host.Client.CallToolAsync(
            "clean_image",
            new Dictionary<string, object?> { ["input_path"] = inputPath });

        AssertSuccess(result);
        result.Content.Should().HaveCount(2);
        result.Content[0].Should().BeOfType<TextContentBlock>();
        result.Content[1].Should().BeOfType<ImageContentBlock>();

        ImageContentBlock image = (ImageContentBlock)result.Content[1];
        image.MimeType.Should().Be("image/png");
        byte[] bytes = image.DecodedData.ToArray();
        bytes.Length.Should().BeGreaterThan(8);

        // First 8 bytes of a PNG file are always the same signature.
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'N');
        bytes[3].Should().Be((byte)'G');
    }

    // -------------------------------------------------------------- detect_watermark

    [Fact]
    public async Task DetectWatermark_ReturnsRegions()
    {
        // A 32x32 PNG with one transparent overlay region (a
        // semi-opaque block) is enough to give the real
        // MaskGenerator something to flag. The result shape is
        // a JSON array of DetectedRegion; we don't pin the
        // exact count (the heuristics are tuned by config) —
        // just that the call returns a non-error result with a
        // parseable array.
        string inputPath = WritePngWithAlphaRegion(32, 32);

        CallToolResult result = await _host.Client.CallToolAsync(
            "detect_watermark",
            new Dictionary<string, object?> { ["input_path"] = inputPath });

        AssertSuccess(result);
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();
        using JsonDocument doc = JsonDocument.Parse(text.Text);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // -------------------------------------------------------------- error mapping

    [Fact]
    public async Task EmptyInput_ReturnsToolError()
    {
        // clean_image with an empty `input_path` triggers the
        // IsNullOrWhiteSpace guard in CleanImageTool, which throws
        // McpException. Per the MCP spec, tool errors are NOT
        // protocol errors — they are signalled via
        // CallToolResult.IsError so the model can self-correct
        // rather than the protocol error path kicking in.
        CallToolResult result = await _host.Client.CallToolAsync(
            "clean_image",
            new Dictionary<string, object?> { ["input_path"] = "" });

        result.IsError.Should().BeTrue(
            "McpException surfaced from a tool becomes CallToolResult.IsError, not a transport error");
        result.Content.Should().NotBeEmpty(
            "the error message should be available in the result content for the agent to read");
    }

    // -------------------------------------------------------------- helpers

    private string TempPath(string name) =>
        Path.Combine(_host.TempDir, $"{Guid.NewGuid():N}-{name}");

    private string WriteSolidPng(int width, int height, SKColor color)
    {
        string path = TempPath("solid.png");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        pixels.Fill(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
        return path;
    }

    private string WritePngWithAlphaRegion(int width, int height)
    {
        // A 32x32 image with a small semi-transparent block at the
        // top-left — exactly the kind of overlay watermark the
        // real MaskGenerator is tuned to catch (alpha-channel
        // analysis).
        string path = TempPath("alpha.png");
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Span<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan());
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = x < 8 && y < 8
                    ? new SKColor(200, 200, 200, 80)   // Semi-transparent pixel — the alpha-channel detector will flag this.
                    : new SKColor(0, 0, 0, 255);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.AsStream().CopyTo(stream);
        return path;
    }

    private string WritePngWithTextChunk(string keyword, string value)
    {
        // Hand-rolled PNG with a single tEXt chunk so the real
        // PngMetadataCleaner has something to strip. Same shape
        // as the fixture used in CleanFileToolTests.
        string path = TempPath("text.png");
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

        byte[] keywordBytes = System.Text.Encoding.ASCII.GetBytes(keyword);
        byte[] valueBytes = System.Text.Encoding.ASCII.GetBytes(value);
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
        uint crc = PngCrc32.Compute(chunk.AsSpan(4, 4 + dataLength));
        WriteUInt32BigEndian(chunk.AsSpan(8 + dataLength, 4), crc);

        using FileStream fs = File.Create(path);
        fs.Write(original, 0, 8); // signature
        fs.Write(chunk, 0, chunk.Length); // tEXt
        fs.Write(original, 8, original.Length - 8); // rest of file
        return path;
    }

    private static void WriteUInt32BigEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 24);
        dest[1] = (byte)(value >> 16);
        dest[2] = (byte)(value >> 8);
        dest[3] = (byte)value;
    }

    private static class PngCrc32
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

/// <summary>
/// xUnit class fixture: hosts the MCP server in-process bound to
/// paired in-memory pipes, then connects an <see cref="McpClient"/>
/// to it via <see cref="StreamClientTransport"/>. One fixture per
/// test class — every test gets a fresh server + client without
/// having to spin them up in <see cref="IAsyncLifetime.InitializeAsync"/>.
/// </summary>
public sealed class McpJsonRpcHost : IAsyncLifetime
{
    private Pipe _c2s = null!;
    private Pipe _s2c = null!;
    private IHost? _host;

    public McpClient Client { get; private set; } = null!;
    public string TempDir { get; } =
        Path.Combine(Path.GetTempPath(), "wr-mcp-rpc-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(TempDir);

        // Two pipes for duplex communication: client→server and
        // server→client. Each end is wrapped as a Stream so the
        // SDK's StreamServerTransport / StreamClientTransport
        // can read & write through it like any other IO.
        _c2s = new Pipe();
        _s2c = new Pipe();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        AppConfig config = AppConfig.Default;
        builder.Services.AddSingleton(config);
        builder.Services.AddWatermarkRemoverCore(config);
        builder.Services.AddWatermarkRemoverText();
        builder.Services.AddWatermarkRemoverMetadata();
        builder.Services.AddWatermarkRemoverImage();
        // Replace the real ONNX inpaint runner with a no-ONNX fake
        // so the host can start without a downloaded model. Same
        // approach used by the rest of the test suite.
        builder.Services.AddSingleton<IInpaintRunner>(_ => new LocalFakeInpaintRunner());

        builder.Services.AddWatermarkRemoverMcp()
            .WithStreamServerTransport(
                _c2s.Reader.AsStream(),
                _s2c.Writer.AsStream());

        _host = builder.Build();
        await _host.StartAsync().ConfigureAwait(false);

        StreamClientTransport transport = new(
            _c2s.Writer.AsStream(),
            _s2c.Reader.AsStream(),
            NullLoggerFactory.Instance);

        Client = await McpClient.CreateAsync(transport).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Client is not null)
            {
                await Client.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (_host is not null)
            {
                await _host.StopAsync().ConfigureAwait(false);
                _host.Dispose();
            }
            _c2s.Writer.Complete();
            _s2c.Writer.Complete();
            _c2s.Reader.Complete();
            _s2c.Reader.Complete();

            if (Directory.Exists(TempDir))
            {
                try
                {
                    Directory.Delete(TempDir, recursive: true);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }

    /// <summary>
    /// In-process <see cref="IInpaintRunner"/> that paints every
    /// masked pixel a fixed colour. Returns <c>"fake"</c> as its
    /// model name so callers can tell at a glance that the real
    /// ONNX LaMa runtime was bypassed.
    /// </summary>
    private sealed class LocalFakeInpaintRunner : IInpaintRunner
    {
        public string ModelName => "fake";
        public bool IsAvailable => true;
        public int InpaintCallCount { get; private set; }

        public SKBitmap Inpaint(SKBitmap image, SKBitmap mask)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(mask);
            InpaintCallCount++;

            SKBitmap output = new(image.Width, image.Height, image.ColorType, SKAlphaType.Opaque);
            ReadOnlySpan<SKColor> inputPixels = MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
            ReadOnlySpan<byte> maskPixels = mask.GetPixelSpan();
            Span<SKColor> outPixels = MemoryMarshal.Cast<byte, SKColor>(output.GetPixelSpan());
            SKColor fill = new(0, 255, 0);
            for (int i = 0; i < inputPixels.Length; i++)
            {
                outPixels[i] = maskPixels[i] > 127 ? fill : inputPixels[i];
            }
            return output;
        }
    }
}
