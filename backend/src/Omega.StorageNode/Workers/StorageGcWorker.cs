using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Workers;

public class StorageGcWorker : BackgroundService
{
    private readonly IStorageService _storageService;
    private readonly StorageNodeOptions _options;
    private readonly ILogger<StorageGcWorker> _logger;

    public StorageGcWorker(
        IStorageService storageService,
        IOptions<StorageNodeOptions> options,
        ILogger<StorageGcWorker> logger)
    {
        _storageService = storageService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Storage GC Worker started. Interval: {Minutes} minutes, Expiration: {Hours} hours",
            _options.GcIntervalMinutes, _options.TempSessionLifetimeHours);

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.GcIntervalMinutes));
        var expiration = TimeSpan.FromHours(Math.Max(1, _options.TempSessionLifetimeHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                _logger.LogDebug("Starting temporary storage garbage collection sweep...");
                var (deletedSessions, deletedFiles) = await _storageService.CleanupExpiredTempDataAsync(expiration, stoppingToken);
                if (deletedSessions > 0 || deletedFiles > 0)
                {
                    _logger.LogInformation("GC sweep completed: purged {Sessions} sessions and {Files} orphaned files",
                        deletedSessions, deletedFiles);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during storage GC sweep");
            }
        }

        _logger.LogInformation("Storage GC Worker stopped.");
    }
}
