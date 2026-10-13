using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Infrastructure.Services;

public class DiskTelemetryProvider : IDiskTelemetryProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiskTelemetryProvider> _logger;

    public DiskTelemetryProvider(
        IConfiguration configuration,
        ILogger<DiskTelemetryProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public DiskSpaceInfo GetMasterDiskSpace()
    {
        var storagePath = _configuration["Storage:LocalRootPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "storage");
        try
        {
            var fullPath = Path.GetFullPath(storagePath);

            // 1. Try resolving directory directly (or walk up to nearest existing parent)
            // On modern .NET (Windows & Linux), DriveInfo(path) works for subdirectories and mount points.
            var candidate = fullPath;
            while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
            {
                var parent = Directory.GetParent(candidate);
                candidate = parent?.FullName;
            }

            if (!string.IsNullOrEmpty(candidate))
            {
                try
                {
                    var drive = new DriveInfo(candidate);
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        return new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
                    }
                }
                catch
                {
                    // Fall back to drive root if direct path constructor throws
                }
            }

            // 2. Fall back to root if path-based DriveInfo failed
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    return new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve drive telemetry for storage path '{Path}'", storagePath);
        }

        // Fallback default: 100 GB total, 80 GB free
        return new DiskSpaceInfo(100L * 1024 * 1024 * 1024, 80L * 1024 * 1024 * 1024);
    }
}
