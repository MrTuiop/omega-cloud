namespace OmegaCloud.Infrastructure.Storage;

public class LocalStorageOptions
{
    public const string SectionName = "Storage";

    public string LocalRootPath { get; set; } = "./data/storage";
}
