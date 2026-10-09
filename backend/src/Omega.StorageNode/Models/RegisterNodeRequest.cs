namespace Omega.StorageNode.Models;

public class RegisterNodeRequest
{
    public string RegistrationKey { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsBehindNat { get; set; }
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
}
