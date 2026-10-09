namespace OmegaCloud.Infrastructure.Options;

public class UploadGcOptions
{
    public const string SectionName = "UploadGc";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 30;
    public int DeadSessionRetentionDays { get; set; } = 7;
}
