using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Services;
using OmegaCloud.Infrastructure.Options;

namespace OmegaCloud.Infrastructure.Workers;

public class UploadSessionGcWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<UploadGcOptions> _options;
    private readonly ILogger<UploadSessionGcWorker> _logger;

    public UploadSessionGcWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<UploadGcOptions> options,
        ILogger<UploadSessionGcWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UploadSessionGcWorker background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;

            if (currentOptions.Enabled)
            {
                try
                {
                    _logger.LogInformation("Starting upload sessions and chunk GC check.");

                    using var scope = _scopeFactory.CreateScope();
                    var gcService = scope.ServiceProvider.GetRequiredService<IUploadGcService>();

                    var result = await gcService.CleanupExpiredSessionsAsync(
                        TimeSpan.FromDays(currentOptions.DeadSessionRetentionDays),
                        stoppingToken);

                    if (result.ExpiredSessionsCount > 0 || result.CleanedTempFoldersCount > 0 || result.RemovedOldSessionsCount > 0)
                    {
                        _logger.LogInformation(
                            "Upload GC finished: expired {Expired} sessions, cleaned {Folders} temp folders, removed {Removed} dead session records.",
                            result.ExpiredSessionsCount,
                            result.CleanedTempFoldersCount,
                            result.RemovedOldSessionsCount);
                    }
                    else
                    {
                        _logger.LogDebug("Upload GC found no expired or stale sessions.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred during upload sessions GC.");
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

        _logger.LogInformation("UploadSessionGcWorker background service stopped.");
    }
}
