namespace WatermarkRemover.Core.Models;

/// <summary>Structured error returned by any operation that fails.</summary>
public record ErrorResult(string ErrorCode, string Message, string? Details = null)
{
    public static ErrorResult FromException(string errorCode, Exception ex) =>
        new(errorCode, ex.Message, ex.ToString());
}

/// <summary>Well-known error codes surfaced by the application.</summary>
public static class ErrorCodes
{
    public const string InvalidInput = "INVALID_INPUT";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
    public const string CorruptFile = "CORRUPT_FILE";
    public const string ModelMissing = "MODEL_MISSING";
    public const string ModelDownloadFailed = "MODEL_DOWNLOAD_FAILED";
    public const string InferenceFailed = "INFERENCE_FAILED";
    public const string NetworkError = "NETWORK_ERROR";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string Unknown = "UNKNOWN";
}
