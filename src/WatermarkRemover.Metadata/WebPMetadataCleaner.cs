using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips EXIF, XMP and ICC color-profile chunks from WebP files at the RIFF/VP8X chunk level,
/// preserving the bitstream (VP8 / VP8L), alpha channel (ALPH) and animation chunks (ANIM / ANMF)
/// bit-for-bit. The VP8X feature flags are updated in lock-step with the chunk removals so the
/// output is a self-consistent WebP file.
/// </summary>
/// <remarks>
/// <para>
/// WebP is a RIFF container. Structure:
/// <code>
/// "RIFF" | fileSize-8 (LE u32) | "WEBP" | chunks...
/// chunk = FourCC (4) | dataSize (LE u32) | data | pad-to-even
/// </code>
/// </para>
/// <para>
/// VP8X feature flags live in the first byte of the VP8X data section. EXIF = 0x08, XMP = 0x04,
/// ICC = 0x20. When the corresponding chunk is removed, the matching flag is cleared so the
/// file is not "lying" about its metadata.
/// </para>
/// </remarks>
public sealed class WebPMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".webp"];

    // VP8X feature-flag bits (LSB of the flags uint32, which is the first data byte of the chunk).
    private const byte ExifFlag = 0x08;
    private const byte XmpFlag = 0x04;
    private const byte IccFlag = 0x20;

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) => Extensions.Contains(extension.ToLowerInvariant());

    public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sw = Stopwatch.StartNew();
        byte[] input = ReadFile(inputPath);
        long inputSize = input.LongLength;

        var removed = new List<MetadataEntry>();
        byte[] output = Process(input, options, removed, inspectOnly: false);

        string finalOut = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
        File.WriteAllBytes(finalOut, output);
        sw.Stop();
        return new FileCleanResult(inputPath, finalOut, removed, inputSize, output.LongLength, sw.Elapsed);
    }

    public IReadOnlyList<MetadataEntry> Inspect(string inputPath)
    {
        byte[] input = ReadFile(inputPath);
        var found = new List<MetadataEntry>();
        Process(input, new MetadataCleanOptions(), found, inspectOnly: true);
        return found;
    }

    private static byte[] ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new MetadataStripException($"File not found: {path}") { FilePath = path };
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (!IsValidWebP(bytes))
        {
            throw new MetadataStripException($"Not a valid WebP file: {path}") { FilePath = path };
        }

        return bytes;
    }

    private static bool IsValidWebP(byte[] bytes)
    {
        return bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
    }

    private static byte[] Process(byte[] data, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        using var output = new MemoryStream();

        // Copy the RIFF + WEBP container header (size field is patched at the end).
        output.Write(data, 0, 12);

        // Position of the VP8X flag byte inside `output`, or -1 if the file has no VP8X chunk.
        // The flag byte is the first byte of the VP8X data section (LSB of the LE u32 flags).
        int vp8xFlagPos = -1;
        bool removedExif = false;
        bool removedXmp = false;
        bool removedIcc = false;

        int pos = 12;
        try
        {
            while (pos + 8 <= data.Length)
            {
                string fourcc = Encoding.ASCII.GetString(data, pos, 4);
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));

                if (chunkSize > int.MaxValue)
                {
                    throw new MetadataStripException("WebP chunk size exceeds 2GB.");
                }

                int dataStart = pos + 8;
                int paddedSize = (int)((chunkSize + 1) & ~1u);
                int chunkTotal = 8 + paddedSize;

                if (dataStart + paddedSize > data.Length)
                {
                    throw new MetadataStripException("Truncated WebP chunk.");
                }

                bool remove = ShouldRemove(fourcc, options, out string container, out string key, out string value);
                if (remove)
                {
                    removed.Add(new MetadataEntry(container, key, value));
                    if (fourcc == "EXIF")
                    {
                        removedExif = true;
                    }
                    else if (fourcc == "XMP ")
                    {
                        removedXmp = true;
                    }
                    else if (fourcc == "ICCP")
                    {
                        removedIcc = true;
                    }
                }
                else if (!inspectOnly)
                {
                    if (fourcc == "VP8X")
                    {
                        // Record where the flag byte will land so we can patch flags post-hoc.
                        vp8xFlagPos = (int)output.Position + 8;
                    }

                    output.Write(data, pos, chunkTotal);
                }

                pos += chunkTotal;
            }
        }
        catch (MetadataStripException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new MetadataStripException("Corrupt WebP structure encountered while stripping metadata.", ex);
        }

        if (inspectOnly)
        {
            return [];
        }

        // Patch the RIFF size (file size minus the 8-byte RIFF header).
        long totalSize = output.Length;
        Span<byte> riffSizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(riffSizeBytes, (uint)(totalSize - 8));
        output.Position = 4;
        output.Write(riffSizeBytes);

        // Patch VP8X flags so the file is self-consistent with the chunk removals.
        if (vp8xFlagPos >= 0 && (removedExif || removedXmp || removedIcc))
        {
            output.Position = vp8xFlagPos;
            int current = output.ReadByte();
            if (current < 0)
            {
                throw new MetadataStripException("Could not read VP8X flag byte.");
            }

            byte flags = (byte)current;
            if (removedExif)
            {
                flags &= unchecked((byte)~ExifFlag);
            }

            if (removedXmp)
            {
                flags &= unchecked((byte)~XmpFlag);
            }

            if (removedIcc)
            {
                flags &= unchecked((byte)~IccFlag);
            }

            output.Position = vp8xFlagPos;
            output.WriteByte(flags);
        }

        return output.ToArray();
    }

    private static bool ShouldRemove(string fourcc, MetadataCleanOptions options, out string container, out string key, out string value)
    {
        container = "WebP";
        key = fourcc;
        value = $"{fourcc} chunk";

        switch (fourcc)
        {
            case "EXIF":
                container = "EXIF";
                value = "EXIF metadata block";
                return options.StripExif;

            case "XMP ":
                container = "XMP";
                value = "XMP packet";
                return options.StripXmp;

            case "ICCP":
                container = "ICC";
                value = "ICC color profile";
                return !options.PreserveColorProfile;

            default:
                return false;
        }
    }
}
