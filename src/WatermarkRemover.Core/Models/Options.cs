namespace WatermarkRemover.Core.Models;

/// <summary>Options controlling which text-cleaning layers run.</summary>
public record TextCleanOptions
{
    /// <summary>Layer A — Unicode hygiene.</summary>
    public bool EnableUnicode { get; init; } = true;

    /// <summary>Layer B — statistical / green-list rewrite.</summary>
    public bool EnableStatistical { get; init; }

    /// <summary>Layer C — vendor-specific detection &amp; removal.</summary>
    public bool EnableVendorSpecific { get; init; } = true;

    /// <summary>When set, Layer B will attempt LLM back-translation via this endpoint.</summary>
    public string? LlmEndpoint { get; init; }

    /// <summary>Model name to use for LLM back-translation.</summary>
    public string? LlmModel { get; init; }

    /// <summary>Enable heuristic paraphrasing in Layer B (word shuffle / synonym swap).</summary>
    public bool EnableHeuristicParaphrase { get; init; } = true;
}

/// <summary>Options controlling metadata stripping.</summary>
public record MetadataCleanOptions
{
    public bool StripExif { get; init; } = true;
    public bool StripXmp { get; init; } = true;
    public bool StripIptc { get; init; } = true;
    public bool StripC2pa { get; init; } = true;
    public bool StripMakerNotes { get; init; } = true;
    public bool PreserveColorProfile { get; init; } = true;

    /// <summary>Optional path to a replacement C2PA manifest to inject after stripping.</summary>
    public string? ReplaceC2paManifestPath { get; init; }
}

/// <summary>Options controlling image inpainting.</summary>
public record ImageCleanOptions
{
    /// <summary>Path to the big-lama ONNX model.</summary>
    public string ModelPath { get; init; } = "./models/big_lama_regular_inpaint.onnx";

    /// <summary>Optional explicit mask path (white = inpaint).</summary>
    public string? MaskPath { get; init; }

    /// <summary>Auto-detection confidence threshold (0-1).</summary>
    public double AutoDetectThreshold { get; init; } = 0.4;

    /// <summary>Blend inpainted region edges with a soft alpha for seamless compositing.</summary>
    public bool BlendEdges { get; init; } = true;

    /// <summary>Resolution the ONNX model expects (square, power of two).</summary>
    public int ModelResolution { get; init; } = 512;
}
