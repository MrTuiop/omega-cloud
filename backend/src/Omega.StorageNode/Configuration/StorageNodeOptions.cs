namespace Omega.StorageNode.Configuration;

public class StorageNodeOptions
{
    public const string SectionName = "StorageNode";

    public string MasterBaseUrl { get; set; } = "http://localhost:5000";
    public string? RegistrationKey { get; set; }
    public Guid? NodeId { get; set; }
    public string? NodeSecret { get; set; }
    public string? NodeName { get; set; }
    public string? BaseUrl { get; set; }
    public bool? IsBehindNat { get; set; }
    public string StorageRootPath { get; set; } = "./data/storage";
    public int HeartbeatIntervalSeconds { get; set; } = 20;
    public int TempSessionLifetimeHours { get; set; } = 24;
    public int GcIntervalMinutes { get; set; } = 30;
    public string AllowedOrigins { get; set; } = "*";
    public string StateFileName { get; set; } = "node-state.json";

    public bool ResolveIsBehindNat()
    {
        if (IsBehindNat.HasValue)
        {
            return IsBehindNat.Value;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return true;
        }

        return !BaseUrl.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
