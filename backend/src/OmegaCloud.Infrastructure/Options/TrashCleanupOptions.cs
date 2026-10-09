namespace OmegaCloud.Infrastructure.Options;

public class TrashCleanupOptions
{
    public const string SectionName = "TrashCleanup";

    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int CheckIntervalMinutes { get; set; } = 60;
}
