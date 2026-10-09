namespace OmegaCloud.Application.DTOs.Nodes;

public class NodeRegistrationTokenDto
{
    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegistrationKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
