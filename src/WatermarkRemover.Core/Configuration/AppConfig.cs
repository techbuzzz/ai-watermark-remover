namespace WatermarkRemover.Core.Configuration;

/// <summary>Root application configuration (deserialized from config.yaml).</summary>
public sealed class AppConfig
{
    public TextConfig Text { get; set; } = new();
    public MarkdownConfig Markdown { get; set; } = new();
    public ImageConfig Image { get; set; } = new();
    public MetadataConfig Metadata { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();

    public static AppConfig Default { get; } = new();
}

public sealed class TextConfig
{
    public TextLayersConfig Layers { get; set; } = new();
    public string LlmEndpoint { get; set; } = "http://localhost:11434";
    public string LlmModel { get; set; } = "llama3";
}

public sealed class TextLayersConfig
{
    public bool Unicode { get; set; } = true;
    public bool Statistical { get; set; }
    public bool VendorSpecific { get; set; } = true;
}

public sealed class MarkdownConfig
{
    public bool StripHeadings { get; set; } = true;
    public bool StripCodeFences { get; set; }
    public bool StripInlineCode { get; set; }
    public bool StripLinks { get; set; }
    public bool StripImages { get; set; } = true;
    public bool StripHtml { get; set; } = true;
    public bool StripFrontmatter { get; set; } = true;
    public bool StripAiSignatures { get; set; } = true;
    public bool StripMentions { get; set; } = true;
    public bool StripUnicodeMd { get; set; } = true;
    public bool StripTrailingWs { get; set; } = true;
    public bool PreserveCodeBlocks { get; set; } = true;
}

public sealed class ImageConfig
{
    public string ModelPath { get; set; } = "./models/big_lama_regular_inpaint.onnx";
    public double AutoDetectThreshold { get; set; } = 0.4;
    public bool BlendEdges { get; set; } = true;
}

public sealed class MetadataConfig
{
    public bool StripC2pa { get; set; } = true;
    public bool StripExif { get; set; } = true;
    public bool StripXmp { get; set; } = true;
    public bool PreserveColorProfile { get; set; } = true;
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string Output { get; set; } = "console";
}
