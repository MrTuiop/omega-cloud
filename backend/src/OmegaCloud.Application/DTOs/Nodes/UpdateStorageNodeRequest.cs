using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.DTOs.Nodes;

public class UpdateStorageNodeRequest
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public bool? IsBehindNat { get; set; }
    public StorageNodeStatus? Status { get; set; }
    public bool? IsActive { get; set; }
}
