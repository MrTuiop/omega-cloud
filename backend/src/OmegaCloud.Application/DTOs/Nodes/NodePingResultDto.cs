namespace OmegaCloud.Application.DTOs.Nodes;

public class NodePingResultDto
{
    public Guid NodeId { get; set; }
    public bool IsReachable { get; set; }
    public int? LatencyMs { get; set; }
    public string Status { get; set; } = string.Empty;
}
