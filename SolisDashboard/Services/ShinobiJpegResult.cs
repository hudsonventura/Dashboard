namespace SolisDashboard.Services;

public sealed class ShinobiJpegResult
{
    public byte[]? Bytes { get; init; }
    public string? Error { get; init; }
    public string? Url { get; init; }
    public string? ContentType { get; init; }
    public int StatusCode { get; init; }
    public bool Success => Bytes is { Length: > 0 } && Error is null;

    public static ShinobiJpegResult Ok(byte[] bytes, string url, string? contentType, int statusCode) => new()
    {
        Bytes = bytes,
        Url = url,
        ContentType = contentType,
        StatusCode = statusCode
    };

    public static ShinobiJpegResult Fail(string error, string? url = null, string? contentType = null, int statusCode = 0) => new()
    {
        Error = error,
        Url = url,
        ContentType = contentType,
        StatusCode = statusCode
    };
}
