using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
        (bool[,] mask, _) = BuildMask(image);
        return ExtractRegions(mask, image.Width, image.Height, threshold);
    }

    /// <summary>Build a boolean watermark mask for an already-loaded image.</summary>
    internal static (bool[,] Mask, int Count) BuildMask(Image<Rgba32> image)
    {
        int width = image.Width;
        int height = image.Height;
        var mask = new bool[height, width];
        int count = 0;

        // Pass 1 — alpha channel: semi-transparent pixels.
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte a = row[x].A;
                    if (a is > 0 and < 250)
                    {
                        mask[y, x] = true;
                    }
                }
            }
        });

        count = CountMask(mask, width, height);

        // Pass 2 — colour frequency (only when alpha found nothing meaningful).
        if (count < width * height * 0.001)
        {
            Array.Clear(mask);
            ColorFrequencyPass(image, mask);
            count = CountMask(mask, width, height);
        }

        return (mask, count);
    }

    private static void ColorFrequencyPass(Image<Rgba32> image, bool[,] mask)
    {
        int width = image.Width;
        int height = image.Height;
        var histogram = new Dictionary<int, int>();

        // Quantize to 5-bit per channel to make the histogram robust.
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    if (p.A < 10)
                    {
                        continue;
                    }

                    int key = ((p.R >> 3) << 10) | ((p.G >> 3) << 5) | (p.B >> 3);
                    histogram[key] = histogram.GetValueOrDefault(key) + 1;
                }
            }
        });

        if (histogram.Count == 0)
        {
            return;
        }

        int total = width * height;
        // The dominant colour is treated as the background.
        int backgroundKey = histogram.MaxBy(kv => kv.Value).Key;

        // Candidate overlay colours: light-ish, moderately frequent, not the background.
        var candidates = histogram
            .Where(kv => kv.Key != backgroundKey)
            .Where(kv => kv.Value > total * 0.005 && kv.Value < total * 0.25)
            .Select(kv => kv.Key)
            .ToHashSet();

        if (candidates.Count == 0)
        {
            return;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    int key = ((p.R >> 3) << 10) | ((p.G >> 3) << 5) | (p.B >> 3);
                    if (candidates.Contains(key))
                    {
                        mask[y, x] = true;
                    }
                }
            }
        });
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
}
