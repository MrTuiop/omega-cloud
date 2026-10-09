namespace OmegaCloud.Application.DTOs.Upload;

public class UploadChunkResultDto
{
    public Guid SessionId { get; set; }
    public int ChunkIndex { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public string Status { get; set; } = "Saved";
}
