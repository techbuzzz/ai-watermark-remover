using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Core.Interfaces;

/// <summary>Generates an inpainting mask (white = region to remove) for an image.</summary>
public interface IMaskGenerator
{
    /// <summary>
    /// Auto-detect candidate watermark regions in the image at <paramref name="imagePath"/>.
    /// Returns the detected regions; <paramref name="maskPngPath"/> receives a written mask PNG when regions are found.
    /// </summary>
    IReadOnlyList<DetectedRegion> Detect(string imagePath, double threshold);
}

/// <summary>Runs LaMa ONNX inpainting inference.</summary>
public interface IInpaintingService
{
    /// <summary>True when the ONNX model is present and the service can run inference.</summary>
    bool IsModelAvailable(string modelPath);
}

/// <summary>Downloads and extracts the ONNX inpainting model.</summary>
public interface IModelDownloader
{
    Task<string> DownloadAsync(string destinationDirectory, bool force = false, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Orchestrates the full image cleaning pipeline: load → mask → resize → infer → blend → save.</summary>
public interface IImageCleaningPipeline
{
    Task<ImageCleanResult> CleanAsync(string inputPath, string outputPath, ImageCleanOptions options, CancellationToken cancellationToken = default);

    /// <summary>Detect (without inpainting) watermark regions in an image.</summary>
    IReadOnlyList<DetectedRegion> Detect(string inputPath, ImageCleanOptions options);
}
