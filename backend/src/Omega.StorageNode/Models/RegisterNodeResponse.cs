namespace Omega.StorageNode.Models;

public class RegisterNodeResponse
{
    public Guid NodeId { get; set; }
    public string NodeSecret { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
}
