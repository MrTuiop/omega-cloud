namespace OmegaCloud.Application.Common.Models.Storage;

public class UploadChunkResult
{
    public int ChunkIndex { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
}
