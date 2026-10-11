namespace OmegaCloud.Application.DTOs.Nodes;

public class StorageNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public bool IsBehindNat { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
    public long MaxCapacityBytes { get; set; }
    public long UsedSpaceBytes => Math.Max(0, TotalSpaceBytes - AvailableSpaceBytes);
    public double FreeSpacePercentage => TotalSpaceBytes > 0 ? Math.Round((double)AvailableSpaceBytes / TotalSpaceBytes * 100, 2) : 0;
    public double UsedSpacePercentage => TotalSpaceBytes > 0 ? Math.Round((double)UsedSpaceBytes / TotalSpaceBytes * 100, 2) : 0;
    public double? CpuLoad { get; set; }
    public string? DiskStatus { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public double SecondsSinceLastHeartbeat { get; set; }
    public bool IsOnline { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
