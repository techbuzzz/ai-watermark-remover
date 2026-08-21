namespace WatermarkRemover.Metadata;

/// <summary>Thrown when a file cannot be processed (corrupt, unsupported or unreadable).</summary>
public sealed class MetadataStripException : Exception
{
    public MetadataStripException(string message) : base(message)
    {
    }

    public MetadataStripException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The path of the file that failed, when known.</summary>
    public string? FilePath { get; init; }
}
