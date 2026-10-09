using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Services;
using OmegaCloud.Infrastructure.Options;

namespace OmegaCloud.Infrastructure.Workers;

public class NodeHealthWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NodeHealthOptions> _options;
    private readonly ILogger<NodeHealthWorker> _logger;

    public NodeHealthWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NodeHealthOptions> options,
        ILogger<NodeHealthWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NodeHealthWorker background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;

            if (currentOptions.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var nodeService = scope.ServiceProvider.GetRequiredService<INodeService>();

                    var result = await nodeService.CheckOfflineNodesAsync(
                        TimeSpan.FromSeconds(currentOptions.OfflineThresholdSeconds),
                        TimeSpan.FromSeconds(currentOptions.DegradedThresholdSeconds),
                        stoppingToken);

                    if (result.MarkedOfflineCount > 0 || result.MarkedDegradedCount > 0)
                    {
                        _logger.LogInformation(
                            "Node health monitor: {Offline} nodes went offline, {Degraded} nodes marked degraded.",
                            result.MarkedOfflineCount,
                            result.MarkedDegradedCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred during storage node health check.");
                }
            }

            var interval = TimeSpan.FromSeconds(Math.Max(5, currentOptions.CheckIntervalSeconds));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("NodeHealthWorker background service stopped.");
    }
}
