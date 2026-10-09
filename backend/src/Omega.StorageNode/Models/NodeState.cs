namespace Omega.StorageNode.Models;

public class NodeState
{
    public Guid NodeId { get; set; }
    public string NodeSecret { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsBehindNat { get; set; }
    public DateTime RegisteredAt { get; set; }
}
