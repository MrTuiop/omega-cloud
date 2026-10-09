using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.DTOs.Nodes;

public class NodeHeartbeatRequest
{
    public Guid? NodeId { get; set; }
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
    public double? CpuLoad { get; set; }
    public string? DiskStatus { get; set; }
    public string? BaseUrl { get; set; }
    public StorageNodeStatus? Status { get; set; }
}
