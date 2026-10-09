namespace Omega.StorageNode.Models;

public class HeartbeatResponseDto
{
    public Guid NodeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime AcknowledgedAt { get; set; }
    public int NextHeartbeatIntervalSeconds { get; set; } = 20;
}
