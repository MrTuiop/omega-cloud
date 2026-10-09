namespace Omega.StorageNode.Models;

public class PingResponse
{
    public string Status { get; set; } = "Healthy";
    public Guid NodeId { get; set; }
    public DateTime Timestamp { get; set; }
}
