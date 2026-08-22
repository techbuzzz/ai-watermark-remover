using System.Buffers.Binary;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Metadata;

/// <summary>
/// Strips EXIF, XMP, IPTC and ICC color-profile metadata from TIFF (Tagged Image File Format)
/// files. The image is loaded via <see cref="SixLabors.ImageSharp"/>, the relevant
/// <see cref="ImageMetadata"/> profiles are cleared according to the supplied
/// <see cref="MetadataCleanOptions"/>, and the image is re-encoded as TIFF.
/// </summary>
/// <remarks>
/// <para>
/// TIFF is a tagged container that allows arbitrarily-nested IFDs (Image File Directories). A
/// pure byte-level IFD walk could in theory preserve the on-disk bitstream byte-for-byte, but
/// that requires walking sub-IFDs, recomputing offsets, and handling BigTIFF (64-bit offsets),
/// multi-page IFD chains, and strip/tile-based pixel layouts. The ImageSharp-based approach
/// re-encodes through the standard RGBA pixel pipeline, which is significantly simpler and is
/// lossless for the compression types ImageSharp emits natively (LZW / Deflate / uncompressed).
/// </para>
/// <para>
/// Only single-frame TIFFs are supported. Multi-page TIFFs load as the first IFD only — this
/// matches <see cref="Image.Load(Stream)"/>'s default behaviour and is the same trade-off the
/// project's <c>ImageCleaningPipeline</c> makes when loading images. BigTIFF (magic 43) is
/// supported through ImageSharp's auto-detection.
/// </para>
/// </remarks>
public sealed class TiffMetadataCleaner : IFileMetadataCleaner
{
    private static readonly string[] Extensions = [".tif", ".tiff"];

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanHandle(string extension) =>
        Extensions.Contains(extension.ToLowerInvariant());

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
        if (!IsValidTiff(bytes))
        {
            throw new MetadataStripException($"Not a valid TIFF file: {path}") { FilePath = path };
        }

        return bytes;
    }

    /// <summary>
    /// Validates the TIFF header: a 2-byte byte-order marker (II / MM) followed by a 2-byte
    /// magic number — 42 for classic TIFF or 43 for BigTIFF.
    /// </summary>
    private static bool IsValidTiff(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return false;
        }

        bool littleEndian = bytes[0] == (byte)'I' && bytes[1] == (byte)'I';
        bool bigEndian = bytes[0] == (byte)'M' && bytes[1] == (byte)'M';
        if (!littleEndian && !bigEndian)
        {
            return false;
        }

        ushort magic = littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
        return magic is 42 or 43;
    }

    private static byte[] Process(byte[] data, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        using var input = new MemoryStream(data, writable: false);
        using var image = LoadTiff(input);

        // ImageSharp surfaces per-frame metadata for TIFF (the IFD0 tags land on the first
        // frame's ExifProfile, and the IPTC / XMP / ICC profiles are derived from the same
        // ExifProfile inside the decoder). We strip the same way for every frame so multi-page
        // TIFFs lose the metadata on every page, not just the first.
        foreach (ImageFrame frame in image.Frames)
        {
            StripFrame(frame, options, removed, inspectOnly);
        }

        if (inspectOnly)
        {
            return [];
        }

        using var output = new MemoryStream();
        // Re-encode via the explicit TIFF encoder so the output container is deterministic
        // and decoupled from the decoded `IImageFormat` guess.
        image.Save(output, new TiffEncoder());
        return output.ToArray();
    }

    private static void StripFrame(ImageFrame frame, MetadataCleanOptions options, List<MetadataEntry> removed, bool inspectOnly)
    {
        // EXIF — ImageSharp's `ExifProfile` on a frame carries the IFD0 entries (Width,
        // Length, BitsPerSample, …) plus the EXIF sub-IFD and the GPS sub-IFD. We only
        // report / strip when the profile actually carries EXIF-specific content, which is
        // exposed via the `Parts` flag (`ExifTags | GpsTags | InteropTags`).
        ExifProfile? exif = frame.Metadata.ExifProfile;
        if (exif is not null && HasExifContent(exif))
        {
            if (options.StripExif)
            {
                removed.Add(new MetadataEntry("EXIF", "Exif", "EXIF IFD"));
                if (!inspectOnly)
                {
                    frame.Metadata.ExifProfile = null;
                }
            }
        }

        // XMP — decoded from EXIF tag 0x02BC.
        if (frame.Metadata.XmpProfile is not null)
        {
            if (options.StripXmp)
            {
                removed.Add(new MetadataEntry("XMP", "XMP", "XMP packet"));
                if (!inspectOnly)
                {
                    frame.Metadata.XmpProfile = null;
                }
            }
        }

        // IPTC — decoded from the EXIF IPTC tag.
        if (frame.Metadata.IptcProfile is not null)
        {
            if (options.StripIptc)
            {
                removed.Add(new MetadataEntry("IPTC", "IPTC", "IPTC NAA record"));
                if (!inspectOnly)
                {
                    frame.Metadata.IptcProfile = null;
                }
            }
        }

        // ICC color profile — decoded from the EXIF ICCProfile tag (TIFF tag 0x8773).
        if (frame.Metadata.IccProfile is not null)
        {
            if (!options.PreserveColorProfile)
            {
                removed.Add(new MetadataEntry("ICC", "ICC_PROFILE", "ICC color profile"));
                if (!inspectOnly)
                {
                    frame.Metadata.IccProfile = null;
                }
            }
        }
    }

    /// <summary>
    /// Tags that ImageSharp's TIFF encoder writes as the structural IFD0 baseline. These
    /// are required to render the image and must not be reported as "metadata to strip".
    /// </summary>
    private static readonly HashSet<ushort> StructuralTiffTags = new()
    {
        0x0100, // ImageWidth
        0x0101, // ImageLength
        0x0102, // BitsPerSample
        0x0103, // Compression
        0x0106, // PhotometricInterpretation
        0x0111, // StripOffsets
        0x0115, // SamplesPerPixel
        0x0116, // RowsPerStrip
        0x0117, // StripByteCounts
        0x011A, // XResolution
        0x011B, // YResolution
        0x011C, // PlanarConfiguration
        0x0128, // ResolutionUnit
        0x0131, // Software (always added by ImageSharp's encoder)
    };

    private static bool HasExifContent(ExifProfile profile)
    {
        // The `ExifProfile.Parts` flag is unreliable here — ImageSharp initializes it to
        // `ExifParts.All` for every profile (see ExifProfile ctor in v3.1.12), so we cannot
        // use it to distinguish "real EXIF content" from "basic IFD entries only". Instead,
        // walk the values and check whether the profile carries any tag outside the known
        // structural TIFF IFD0 set.
        foreach (IExifValue value in profile.Values)
        {
            ushort tag = (ushort)value.Tag;
            if (!StructuralTiffTags.Contains(tag))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Loads a TIFF from <paramref name="input"/>, translating the ImageSharp decode-time
    /// exceptions into the project-wide <see cref="MetadataStripException"/> so callers have
    /// a single exception type to handle.
    /// </summary>
    private static Image LoadTiff(Stream input)
    {
        try
        {
            return Image.Load(input);
        }
        catch (Exception ex) when (ex is SixLabors.ImageSharp.ImageFormatException
                                       or InvalidImageContentException
                                       or NotSupportedException
                                       or ArgumentException)
        {
            throw new MetadataStripException("Corrupt TIFF structure encountered while stripping metadata.", ex);
        }
    }
}
