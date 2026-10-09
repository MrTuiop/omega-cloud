namespace OmegaCloud.Application.DTOs.Admin;

public class SystemHealthDto
{
    public double CpuLoadPercentage { get; set; }
    public long UsedMemoryBytes { get; set; }
    public long TotalMemoryBytes { get; set; }
    public double MemoryUsagePercentage { get; set; }
    public long StorageFreeBytes { get; set; }
    public long StorageTotalBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public double StorageUsagePercentage { get; set; }
    public string DatabaseStatus { get; set; } = "Healthy";
    public int DatabaseLatencyMs { get; set; }
    public int TotalNodes { get; set; }
    public int OnlineNodes { get; set; }
    public int OfflineNodes { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveSessionsCount { get; set; }
    public double ServerUptimeSeconds { get; set; }
    public string OsDescription { get; set; } = string.Empty;
    public string FrameworkDescription { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}
