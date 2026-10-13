using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Infrastructure.Services;
using Xunit;

namespace OmegaCloud.Tests.Storage;

public class DiskTelemetryProviderTests
{
    [Fact]
    public void GetMasterDiskSpace_DefaultConfiguration_ReturnsPositiveDiskSpace()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new DiskTelemetryProvider(config, NullLogger<DiskTelemetryProvider>.Instance);

        var info = provider.GetMasterDiskSpace();

        Assert.NotNull(info);
        Assert.True(info.TotalBytes > 0, "TotalBytes should be positive");
        Assert.True(info.AvailableFreeBytes >= 0, "AvailableFreeBytes should be non-negative");
        Assert.True(info.AvailableFreeBytes <= info.TotalBytes, "AvailableFreeBytes should not exceed TotalBytes");
    }

    [Fact]
    public void GetMasterDiskSpace_WithNonExistentSubpath_ResolvesViaWalkUp()
    {
        var tempNonExistentPath = Path.Combine(Directory.GetCurrentDirectory(), "non_existent_folder_" + Guid.NewGuid().ToString("N"), "deep_sub");
        var inMemory = new Dictionary<string, string?>
        {
            ["Storage:LocalRootPath"] = tempNonExistentPath
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var provider = new DiskTelemetryProvider(config, NullLogger<DiskTelemetryProvider>.Instance);

        var info = provider.GetMasterDiskSpace();

        Assert.NotNull(info);
        Assert.True(info.TotalBytes > 0);
        Assert.True(info.AvailableFreeBytes >= 0);
    }

    [Fact]
    public void GetMasterDiskSpace_WithInvalidDrive_ReturnsFallbackValues()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Storage:LocalRootPath"] = "Z:\\non_existent_drive_omega_test\\data"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var provider = new DiskTelemetryProvider(config, NullLogger<DiskTelemetryProvider>.Instance);

        var info = provider.GetMasterDiskSpace();

        Assert.NotNull(info);
        // Fallback is 100 GB total, 80 GB free
        Assert.Equal(100L * 1024 * 1024 * 1024, info.TotalBytes);
        Assert.Equal(80L * 1024 * 1024 * 1024, info.AvailableFreeBytes);
    }
}
