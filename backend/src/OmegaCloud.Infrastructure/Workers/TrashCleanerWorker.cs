using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Services;
using OmegaCloud.Infrastructure.Options;

namespace OmegaCloud.Infrastructure.Workers;

public class TrashCleanerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TrashCleanupOptions> _options;
    private readonly ILogger<TrashCleanerWorker> _logger;

    public TrashCleanerWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TrashCleanupOptions> options,
        ILogger<TrashCleanerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TrashCleanerWorker background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;

            if (currentOptions.Enabled)
            {
                try
                {
                    _logger.LogInformation("Starting trash auto-cleanup check for items older than {Days} days.", currentOptions.RetentionDays);

                    using var scope = _scopeFactory.CreateScope();
                    var trashService = scope.ServiceProvider.GetRequiredService<ITrashService>();

                    var result = await trashService.CleanupExpiredTrashAsync(
                        TimeSpan.FromDays(currentOptions.RetentionDays),
                        stoppingToken);

                    if (result.PurgedFilesCount > 0 || result.PurgedFoldersCount > 0)
                    {
                        _logger.LogInformation(
                            "Trash auto-cleanup finished: purged {FilesCount} files, {FoldersCount} folders, freed {FreedBytes} bytes, removed {BlobsCount} blobs.",
                            result.PurgedFilesCount,
                            result.PurgedFoldersCount,
                            result.FreedBytes,
                            result.PurgedBlobsCount);
                    }
                    else
                    {
                        _logger.LogDebug("Trash auto-cleanup found no expired items to purge.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred during trash auto-cleanup.");
                }
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, currentOptions.CheckIntervalMinutes));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("TrashCleanerWorker background service stopped.");
    }
}
