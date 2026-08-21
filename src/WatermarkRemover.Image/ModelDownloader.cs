using System.IO.Compression;
using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.Image;

/// <summary>Downloads the big-lama model archive from HuggingFace and extracts the ONNX file.</summary>
public sealed class ModelDownloader(HttpClient? httpClient = null) : IModelDownloader
{
    /// <summary>Default source archive (LaMa big-lama).</summary>
    public const string DefaultUrl = "https://huggingface.co/smartywu/big-lama/resolve/main/big-lama.zip";

    /// <summary>Canonical local file name for the extracted ONNX model.</summary>
    public const string ModelFileName = "big_lama_regular_inpaint.onnx";

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

    /// <inheritdoc />
    public async Task<string> DownloadAsync(string destinationDirectory, bool force = false, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        string modelPath = Path.Combine(destinationDirectory, ModelFileName);
        if (File.Exists(modelPath) && !force)
        {
            progress?.Report(1.0);
            return modelPath;
        }

        string tempZip = Path.Combine(Path.GetTempPath(), $"big-lama-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(DefaultUrl, tempZip, progress, cancellationToken).ConfigureAwait(false);
            string? extracted = ExtractOnnx(tempZip, destinationDirectory, modelPath);
            if (extracted is null)
            {
                throw new InvalidOperationException(
                    "The downloaded archive did not contain an .onnx model. " +
                    "Convert the LaMa checkpoint to ONNX or supply an ONNX model manually.");
            }

            return extracted;
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                try
                {
                    File.Delete(tempZip);
                }
                catch (IOException)
                {
                    // best-effort cleanup
                }
            }
        }
    }

    private async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report(Math.Min(1.0, (double)readTotal / total.Value));
            }
        }

        progress?.Report(1.0);
    }

    private static string? ExtractOnnx(string zipPath, string destinationDirectory, string modelPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? onnxEntry = archive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        if (onnxEntry is null)
        {
            return null;
        }

        onnxEntry.ExtractToFile(modelPath, overwrite: true);
        return modelPath;
    }
}
