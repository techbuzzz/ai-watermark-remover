using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

    public LamaInpaintingService(string modelPath)
    {
        _modelPath = modelPath;
        _session = new Lazy<InferenceSession?>(CreateSession);
    }

    public string ModelName => IsAvailable ? "big-lama" : "none";

    public bool IsAvailable => File.Exists(_modelPath) && _session.Value is not null;

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
    public Image<Rgb24> Inpaint(Image<Rgb24> image, Image<L8> mask)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);

        InferenceSession session = _session.Value
            ?? throw new InvalidOperationException("ONNX inpainting model is not available.");

        int h = image.Height;
        int w = image.Width;

        // image tensor: [1,3,H,W] float32 in [0,1]
        var imageTensor = new DenseTensor<float>([1, 3, h, w]);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    Rgb24 p = row[x];
                    imageTensor[0, 0, y, x] = p.R / 255f;
                    imageTensor[0, 1, y, x] = p.G / 255f;
                    imageTensor[0, 2, y, x] = p.B / 255f;
                }
            }
        });

        // mask tensor: [1,1,H,W] float32 (1 = inpaint)
        var maskTensor = new DenseTensor<float>([1, 1, h, w]);
        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                Span<L8> row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    maskTensor[0, 0, y, x] = row[x].PackedValue > 127 ? 1f : 0f;
                }
            }
        });

        string imageInputName = session.InputNames.Count > 0 ? session.InputNames[0] : "image";
        string maskInputName = session.InputNames.Count > 1 ? session.InputNames[1] : "mask";

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(imageInputName, imageTensor),
            NamedOnnxValue.CreateFromTensor(maskInputName, maskTensor),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
        Tensor<float> output = results.First().AsTensor<float>();

        return TensorToImage(output, w, h);
    }

    /// <summary>Convert the model output (NHWC, values in [0,255] or [0,1]) back to an image.</summary>
    private static Image<Rgb24> TensorToImage(Tensor<float> output, int w, int h)
    {
        int[] dims = [.. output.Dimensions];
        bool nhwc = dims.Length == 4 && dims[3] == 3;
        var result = new Image<Rgb24>(w, h);

        // Detect value range by sampling the first element.
        float sample = output.GetValue(0);
        float scale = sample > 1.5f ? 1f : 255f;

        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
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

                    row[x] = new Rgb24(ClampByte(r * scale), ClampByte(g * scale), ClampByte(b * scale));
                }
            }
        });

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
