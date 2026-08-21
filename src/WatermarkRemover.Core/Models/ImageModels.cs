namespace WatermarkRemover.Core.Models;

/// <summary>Result of removing visual watermarks from an image.</summary>
public record ImageCleanResult(
    string InputPath,
    string OutputPath,
    IReadOnlyList<DetectedRegion> DetectedWatermarks,
    int InputWidth,
    int InputHeight,
    int OutputWidth,
    int OutputHeight,
    TimeSpan ProcessingTime,
    string ModelUsed
);

/// <summary>A detected watermark region within an image.</summary>
public record DetectedRegion(int X, int Y, int Width, int Height, double Confidence);
