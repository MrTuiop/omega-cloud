namespace OmegaCloud.Application.Common.Interfaces;

public class ThumbnailResult
{
    public Stream Stream { get; set; } = Stream.Null;
    public string ContentType { get; set; } = "image/webp";
    public string ETag { get; set; } = string.Empty;
}

public interface IThumbnailService
{
    ThumbnailResult? TryGetCachedThumbnail(
        string contentSha256,
        int targetWidth = 256,
        int targetHeight = 256,
        string format = "webp",
        string? mimeType = null);

    Task<ThumbnailResult> GetOrCreateThumbnailAsync(
        Stream sourceStream,
        string fileName,
        string mimeType,
        string contentSha256,
        int targetWidth = 256,
        int targetHeight = 256,
        string format = "webp",
        CancellationToken ct = default);
}
