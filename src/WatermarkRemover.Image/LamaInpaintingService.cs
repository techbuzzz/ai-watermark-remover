using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.Image;

/// <summary>
/// Loads the big-lama ONNX model and runs inpainting inference via ONNX Runtime.
/// Degrades gracefully: when the model file is missing, <see cref="IsAvailable"/> is false and
/// callers should skip inpainting.
/// </summary>
public sealed class LamaInpaintingService : IInpaintRunner, IInpaintingService, IDisposable
{
    private readonly string _modelPath;
    private readonly Lazy<InferenceSession?> _session;

    // Cached once: the model file is not expected to appear or vanish at
    // runtime, so IsAvailable should not stat the disk on every poll. The
    // field is set the first time IsAvailable is evaluated; subsequent
    // reads skip both the File.Exists check and the Lazy.Value touch.
    private int _availabilityCache; // 0 = unknown, 1 = available, -1 = unavailable

    public LamaInpaintingService(string modelPath)
    {
        _modelPath = modelPath;
        _session = new Lazy<InferenceSession?>(CreateSession);
    }

    public string ModelName => IsAvailable ? "big-lama" : "none";

    public bool IsAvailable
    {
        get
        {
            int cached = _availabilityCache;
            if (cached is not 0)
            {
                return cached > 0;
            }

            bool available = File.Exists(_modelPath) && _session.Value is not null;
            _availabilityCache = available ? 1 : -1;
            return available;
        }
    }

    public bool IsModelAvailable(string modelPath) => File.Exists(modelPath);

    private InferenceSession? CreateSession()
    {
        if (!File.Exists(_modelPath))
        {
            return null;
        }

        try
        {
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            return new InferenceSession(_modelPath, options);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public SKBitmap Inpaint(SKBitmap image, SKBitmap mask)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);

        InferenceSession? session = _session.Value;
        if (session is null)
        {
            throw new InvalidOperationException("ONNX inpainting model is not available.");
        }

        int h = image.Height;
        int w = image.Width;

        // image tensor: [1,3,H,W] float32 in [0,1]. Rgb888x is byte-aligned
        // 4 bytes/pixel with RGB at offsets 0,1,2 and alpha at 3 — we read
        // offsets 0..2 and discard the alpha byte. Rgba8888 is byte-aligned
        // 4 bytes/pixel with alpha first; we read the SKColor values
        // directly via the GetPixelSpan<T>() overload.
        var imageTensor = new DenseTensor<float>([1, 3, h, w]);
        if (image.ColorType == SKColorType.Rgb888x)
        {
            ReadOnlySpan<byte> imgBytes = image.GetPixelSpan();
            for (int i = 0; i < imgBytes.Length; i += 4)
            {
                int p = i / 4;
                int x = p % w;
                int y = p / w;
                imageTensor[0, 0, y, x] = imgBytes[i] / 255f;
                imageTensor[0, 1, y, x] = imgBytes[i + 1] / 255f;
                imageTensor[0, 2, y, x] = imgBytes[i + 2] / 255f;
            }
        }
        else
        {
            ReadOnlySpan<SKColor> imgColors = MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
            for (int i = 0; i < imgColors.Length; i++)
            {
                int x = i % w;
                int y = i / w;
                SKColor c = imgColors[i];
                imageTensor[0, 0, y, x] = c.Red / 255f;
                imageTensor[0, 1, y, x] = c.Green / 255f;
                imageTensor[0, 2, y, x] = c.Blue / 255f;
            }
        }

        // mask tensor: [1,1,H,W] float32 (1 = inpaint). We require the mask
        // to be Gray8 — anything else is the caller's bug and we surface it
        // loudly rather than silently producing garbage.
        if (mask.ColorType != SKColorType.Gray8)
        {
            throw new ArgumentException("Inpaint mask must be a Gray8 SKBitmap.", nameof(mask));
        }
        var maskTensor = new DenseTensor<float>([1, 1, h, w]);
        ReadOnlySpan<byte> maskBytes = mask.GetPixelSpan();
        for (int i = 0; i < maskBytes.Length; i++)
        {
            maskTensor[0, 0, 0, i] = maskBytes[i] > 127 ? 1f : 0f;
        }

        string imageInputName = session.InputNames.Count > 0 ? session.InputNames[0] : "image";
        string maskInputName = session.InputNames.Count > 1 ? session.InputNames[1] : "mask";

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(imageInputName, imageTensor),
            NamedOnnxValue.CreateFromTensor(maskInputName, maskTensor),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
        Tensor<float> output = results.First().AsTensor<float>();

        return TensorToBitmap(output, w, h);
    }

    /// <summary>Convert the model output (NCHW or NHWC, values in [0,255] or [0,1]) back to a bitmap.</summary>
    private static SKBitmap TensorToBitmap(Tensor<float> output, int w, int h)
    {
        int[] dims = [.. output.Dimensions];
        bool nhwc = dims.Length == 4 && dims[3] == 3;
        var result = new SKBitmap(w, h, SKColorType.Rgb888x, SKAlphaType.Opaque);
        Span<byte> resultBytes = result.GetPixelSpan();

        // Detect value range by sampling the first element.
        float sample = output.GetValue(0);
        float scale = sample > 1.5f ? 1f : 255f;

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                float r, g, b;
                if (nhwc)
                {
                    r = output[0, y, x, 0];
                    g = output[0, y, x, 1];
                    b = output[0, y, x, 2];
                }
                else
                {
                    r = output[0, 0, y, x];
                    g = output[0, 1, y, x];
                    b = output[0, 2, y, x];
                }

                int o = (rowOffset + x) * 4;
                resultBytes[o] = ClampByte(r * scale);
                resultBytes[o + 1] = ClampByte(g * scale);
                resultBytes[o + 2] = ClampByte(b * scale);
                resultBytes[o + 3] = 0;
            }
        }

        return result;
    }

    private static byte ClampByte(float v) => (byte)Math.Clamp(MathF.Round(v), 0, 255);

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value?.Dispose();
        }
    }
}
