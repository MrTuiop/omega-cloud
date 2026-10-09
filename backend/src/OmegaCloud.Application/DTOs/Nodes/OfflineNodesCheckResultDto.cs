namespace OmegaCloud.Application.DTOs.Nodes;

public class OfflineNodesCheckResultDto
{
    public int CheckedNodesCount { get; set; }
    public int MarkedOfflineCount { get; set; }
    public int MarkedDegradedCount { get; set; }
    public List<Guid> AffectedNodeIds { get; set; } = new();
}
