namespace OmegaCloud.Application.DTOs.Upload;

public class CompleteUploadSessionRequest
{
    public string? ExpectedSha256 { get; set; }
    public OmegaCloud.Domain.Enums.ConflictResolutionStrategy? ConflictResolution { get; set; }
}
