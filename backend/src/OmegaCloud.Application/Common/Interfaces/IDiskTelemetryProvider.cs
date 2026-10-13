namespace OmegaCloud.Application.Common.Interfaces;

public record DiskSpaceInfo(long TotalBytes, long AvailableFreeBytes);

public interface IDiskTelemetryProvider
{
    DiskSpaceInfo GetMasterDiskSpace();
}
