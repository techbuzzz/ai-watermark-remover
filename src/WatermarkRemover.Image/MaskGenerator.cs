using System.Runtime.InteropServices;
using SkiaSharp;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Image;

/// <summary>
/// Auto-detects candidate watermark regions using two heuristics:
/// (1) alpha-channel analysis — semi-transparent pixels are typical overlay watermarks;
/// (2) colour-frequency analysis — a moderately-frequent, clustered overlay colour.
/// </summary>
public sealed class MaskGenerator : IMaskGenerator
{
    /// <inheritdoc />
    public IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image not found", imagePath);
        }

        using SKBitmap image = LoadRgba(imagePath);
        (_, _, IReadOnlyList<DetectedRegion> regions) = BuildMaskWithRegions(image, threshold);
        return regions;
    }

    /// <summary>Build a boolean watermark mask for an already-loaded image.</summary>
    public static (bool[,] Mask, int Count) BuildMask(SKBitmap image)
    {
        (bool[,] mask, int count, _) = BuildMaskWithRegions(image, threshold: 0.0);
        return (mask, count);
    }

    /// <summary>
    /// Build a boolean watermark mask *and* the detected regions in a single
    /// pass. This is the allocation-friendly entry point for the cleaning
    /// pipeline: previously the pipeline called <see cref="BuildMask"/> and
    /// then re-scanned the whole 2D bool array to derive bounding boxes,
    /// duplicating the work <see cref="ExtractRegions"/> already did here.
    /// </summary>
    /// <param name="threshold">Confidence threshold; pass 0.0 to skip region extraction.</param>
    public static (bool[,] Mask, int Count, IReadOnlyList<DetectedRegion> Regions) BuildMaskWithRegions(SKBitmap image, double threshold)
    {
        int width = image.Width;
        int height = image.Height;
        var mask = new bool[height, width];
        int count = 0;

        // Pass 1 — alpha channel: semi-transparent pixels. SKColor.Alpha is
        // 0..255; the range (0, 250) catches the typical "watermark overlay"
        // alpha values (anything > 0 but < fully-opaque) without tripping on
        // accidentally-rounded-up solid pixels. Rgba8888 is 4 bytes per pixel
        // and SKColor is a 4-byte struct, so the byte span reinterprets
        // cleanly.
        ReadOnlySpan<SKColor> pixels = MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                byte a = pixels[rowOffset + x].Alpha;
                if (a is > 0 and < 250)
                {
                    mask[y, x] = true;
                }
            }
        }

        count = CountMask(mask, width, height);

        // Pass 2 — colour frequency (only when alpha found nothing meaningful).
        if (count < width * height * 0.001)
        {
            Array.Clear(mask);
            ColorFrequencyPass(pixels, width, height, mask);
            count = CountMask(mask, width, height);
        }

        // Region extraction is expensive (BFS over the whole mask); only
        // do it when the caller actually wants regions (threshold > 0).
        IReadOnlyList<DetectedRegion> regions = threshold > 0
            ? ExtractRegions(mask, width, height, threshold)
            : [];

        return (mask, count, regions);
    }

    private static void ColorFrequencyPass(ReadOnlySpan<SKColor> pixels, int width, int height, bool[,] mask)
    {
        var histogram = new Dictionary<int, int>();

        // Track the dominant colour while building the histogram in a single
        // pass — avoids a second O(N) MaxBy sweep over the dictionary.
        int backgroundKey = 0;
        int backgroundCount = -1;

        // Quantize to 5-bit per channel to make the histogram robust.
        for (int i = 0; i < pixels.Length; i++)
        {
            SKColor p = pixels[i];
            if (p.Alpha < 10)
            {
                continue;
            }

            int key = ((p.Red >> 3) << 10) | ((p.Green >> 3) << 5) | (p.Blue >> 3);
            int newCount = histogram.GetValueOrDefault(key) + 1;
            histogram[key] = newCount;
            if (newCount > backgroundCount)
            {
                backgroundCount = newCount;
                backgroundKey = key;
            }
        }

        if (histogram.Count == 0)
        {
            return;
        }

        int total = width * height;

        // Candidate overlay colours: light-ish, moderately frequent, not the background.
        var candidates = new HashSet<int>();
        foreach (KeyValuePair<int, int> kv in histogram)
        {
            if (kv.Key == backgroundKey)
            {
                continue;
            }

            if (kv.Value > total * 0.005 && kv.Value < total * 0.25)
            {
                candidates.Add(kv.Key);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                SKColor p = pixels[rowOffset + x];
                int key = ((p.Red >> 3) << 10) | ((p.Green >> 3) << 5) | (p.Blue >> 3);
                if (candidates.Contains(key))
                {
                    mask[y, x] = true;
                }
            }
        }
    }

    private static int CountMask(bool[,] mask, int width, int height)
    {
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask[y, x])
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Group masked pixels into bounding-box regions via connected-component labelling.</summary>
    private static List<DetectedRegion> ExtractRegions(bool[,] mask, int width, int height, double threshold)
    {
        var regions = new List<DetectedRegion>();
        var visited = new bool[height, width];
        var queue = new Queue<(int X, int Y)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[y, x] || visited[y, x])
                {
                    continue;
                }

                int minX = x, minY = y, maxX = x, maxY = y, area = 0;
                queue.Clear();
                queue.Enqueue((x, y));
                visited[y, x] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    area++;
                    if (cx < minX) { minX = cx; }
                    if (cy < minY) { minY = cy; }
                    if (cx > maxX) { maxX = cx; }
                    if (cy > maxY) { maxY = cy; }

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height && mask[ny, nx] && !visited[ny, nx])
                            {
                                visited[ny, nx] = true;
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }
                }

                int boxW = maxX - minX + 1;
                int boxH = maxY - minY + 1;
                double fill = (double)area / (boxW * boxH);
                double confidence = Math.Min(1.0, 0.5 + (fill * 0.5));

                // Ignore tiny specks.
                if (area >= 16 && confidence >= threshold)
                {
                    regions.Add(new DetectedRegion(minX, minY, boxW, boxH, Math.Round(confidence, 3)));
                }
            }
        }

        return regions.OrderByDescending(r => r.Width * r.Height).ToList();
    }

    /// <summary>Decode an image from <paramref name="path"/> and return it as an
    /// RGBA-8888 <see cref="SKBitmap"/>. The caller owns the returned bitmap.</summary>
    private static SKBitmap LoadRgba(string path)
    {
        SKBitmap raw = SKBitmap.Decode(path);
        if (raw.ColorType == SKColorType.Rgba8888)
        {
            return raw;
        }

        // Re-decode into the colour type our pixel iteration assumes.
        SKBitmap converted = raw.Copy(SKColorType.Rgba8888);
        raw.Dispose();
        return converted;
    }
}
