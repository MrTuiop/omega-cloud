namespace OmegaCloud.Application.Common.Models.Storage;

public class FileDownloadResult
{
    public Stream? Stream { get; set; }
    public string? RedirectUrl { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    public bool IsRedirect => !string.IsNullOrEmpty(RedirectUrl);
}
