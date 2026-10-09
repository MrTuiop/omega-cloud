namespace OmegaCloud.Infrastructure.Options;

public class NodeHealthOptions
{
    public const string SectionName = "NodeHealth";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 15;
    public int OfflineThresholdSeconds { get; set; } = 60;
    public int DegradedThresholdSeconds { get; set; } = 30;
}
