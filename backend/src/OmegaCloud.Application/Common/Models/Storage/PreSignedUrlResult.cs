namespace OmegaCloud.Application.Common.Models.Storage;

public class PreSignedUrlResult
{
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public IDictionary<string, string>? Headers { get; set; }
}
