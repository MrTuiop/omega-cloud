namespace Omega.StorageNode.Models;

public class NodeHeartbeatRequest
{
    public Guid? NodeId { get; set; }
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
    public double? CpuLoad { get; set; }
    public string? DiskStatus { get; set; }
    public string? BaseUrl { get; set; }
    public string? Status { get; set; }
}
