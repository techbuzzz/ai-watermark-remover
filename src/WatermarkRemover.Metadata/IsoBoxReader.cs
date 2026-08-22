using System.Buffers.Binary;
using System.Text;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Shared 4CC box header reader for ISO base media file format (ISOBMFF /
/// ISO 14496-12) parsers. The MP4 / MOV / HEIF / AVIF / 3GP / QuickTime
/// families all use the same wire format:
/// <code>
/// box       = size (BE u32) | type (4CC ASCII) | payload
/// FullBox   = box header | version (1) | flags (3) | payload
/// largesize = box header where size == 1, followed by 8-byte BE u64
/// </code>
/// </summary>
/// <remarks>
/// Used by <see cref="Mp4MetadataCleaner"/> and (in future refactors) by
/// <see cref="AvifMetadataCleaner"/>. The walker itself lives in the
/// format-specific cleaner because the policy (which boxes to strip)
/// is per-format; this helper only handles the header decoding.
/// </remarks>
internal static class IsoBoxReader
{
    /// <summary>Decoded view of an ISOBMFF box header.</summary>
    internal readonly record struct BoxHeader(
        int Start,
        int TotalSize,
        int HeaderSize,
        int PayloadStart,
        int PayloadSize,
        string Type);

    /// <summary>
    /// Resolves the size and 4CC type of the box starting at
    /// <paramref name="pos"/>, honouring the 16-byte <c>largesize</c>
    /// extension (size field == 1 followed by an 8-byte BE u64).
    /// </summary>
    /// <param name="data">Full container byte array.</param>
    /// <param name="pos">Byte offset of the box header.</param>
    /// <param name="filePath">Optional path used when raising
    /// <see cref="MetadataStripException"/> for malformed input.</param>
    /// <param name="sizeZeroExtendsToEof">If true, treat size==0 as
    /// "extends to EOF" (ISO 14496-12 §4.2). Defaults to false (refuse,
    /// because top-level boxes that extend to EOF confuse the walker).</param>
    /// <exception cref="MetadataStripException">Truncated or invalid header.</exception>
    internal static BoxHeader Read(
        byte[] data,
        int pos,
        string? filePath = null,
        bool sizeZeroExtendsToEof = false)
    {
        if (pos + 8 > data.Length)
        {
            throw new MetadataStripException("Truncated ISOBMFF box header.")
            {
                FilePath = filePath,
            };
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));

        // 4CC types are 4 raw bytes in Mac OS Roman / ISO-8859-1. We
        // use Latin-1 because it maps every byte 0x00..0xFF 1:1 to a
        // Unicode code point (Mac Roman differs in 0x80..0x9F but
        // shares 0xA9 = © on the user-data fourccs that MP4 / MOV
        // emit). ASCII would substitute "?" for the 0xA9 byte and
        // corrupt the type string for ©xyz / ©day / ©mak / etc.
        string type = Encoding.Latin1.GetString(data, pos + 4, 4);

        int headerSize;
        long boxSize;
        if (size32 == 1)
        {
            // largesize: 8-byte BE u64 follows the type 4CC.
            if (pos + 16 > data.Length)
            {
                throw new MetadataStripException("Truncated ISOBMFF largesize header.")
                {
                    FilePath = filePath,
                };
            }

            boxSize = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(pos + 8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            if (!sizeZeroExtendsToEof)
            {
                throw new MetadataStripException("ISOBMFF box with size==0 is not supported.")
                {
                    FilePath = filePath,
                };
            }

            boxSize = data.Length - pos;
            headerSize = 8;
        }
        else
        {
            boxSize = size32;
            headerSize = 8;
        }

        if (boxSize < headerSize)
        {
            throw new MetadataStripException("ISOBMFF box size is smaller than its header.")
            {
                FilePath = filePath,
            };
        }

        if (pos + boxSize > data.Length)
        {
            throw new MetadataStripException("Truncated ISOBMFF box payload.")
            {
                FilePath = filePath,
            };
        }

        return new BoxHeader(
            Start: pos,
            TotalSize: (int)boxSize,
            HeaderSize: headerSize,
            PayloadStart: pos + headerSize,
            PayloadSize: (int)(boxSize - headerSize),
            Type: type);
    }
}
