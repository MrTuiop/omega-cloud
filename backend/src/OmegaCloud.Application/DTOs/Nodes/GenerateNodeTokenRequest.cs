namespace OmegaCloud.Application.DTOs.Nodes;

public class GenerateNodeTokenRequest
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsBehindNat { get; set; } = false;
}
