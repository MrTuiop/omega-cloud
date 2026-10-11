using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Domain.Entities;

public class StorageNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string RegistrationKey { get; set; } = string.Empty;
    public string NodeSecretHash { get; set; } = string.Empty;
    public StorageNodeType Type { get; set; } = StorageNodeType.LocalDisk;
    public string? BaseUrl { get; set; }
    public bool IsBehindNat { get; set; } = false;
    public StorageNodeStatus Status { get; set; } = StorageNodeStatus.Online;
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
    public long MaxCapacityBytes { get; set; }
    public double? CpuLoad { get; set; }
    public string? DiskStatus { get; set; }
    public int? LatencyMs { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StorageBlob> Blobs { get; set; } = new List<StorageBlob>();
    public ICollection<UploadSession> UploadSessions { get; set; } = new List<UploadSession>();
}
