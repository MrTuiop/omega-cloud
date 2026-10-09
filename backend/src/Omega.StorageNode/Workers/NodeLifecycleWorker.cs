using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Workers;

public class NodeLifecycleWorker : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly INodeStateManager _stateManager;
    private readonly INodeHmacValidator _hmacValidator;
    private readonly IStorageService _storageService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly StorageNodeOptions _options;
    private readonly ILogger<NodeLifecycleWorker> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NodeLifecycleWorker(
        HttpClient httpClient,
        INodeStateManager stateManager,
        INodeHmacValidator hmacValidator,
        IStorageService storageService,
        IDateTimeProvider dateTimeProvider,
        IOptions<StorageNodeOptions> options,
        ILogger<NodeLifecycleWorker> logger)
    {
        _httpClient = httpClient;
        _stateManager = stateManager;
        _hmacValidator = hmacValidator;
        _storageService = storageService;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Storage Node Lifecycle Worker starting...");

        // Phase 1: Registration
        while (!stoppingToken.IsCancellationRequested && !_stateManager.IsConfigured)
        {
            var registered = await TryRegisterAsync(stoppingToken);
            if (registered)
            {
                break;
            }

            _logger.LogWarning("Registration with Master pending. Retrying in 10 seconds...");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        _logger.LogInformation("Storage Node is active with NodeId: {NodeId}. Entering Heartbeat loop...",
            _stateManager.NodeId);

        // Phase 2: Periodic Heartbeat
        var intervalSeconds = _options.HeartbeatIntervalSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextInterval = await SendHeartbeatAsync(stoppingToken);
                if (nextInterval.HasValue && nextInterval.Value > 0)
                {
                    intervalSeconds = nextInterval.Value;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat attempt failed. Will retry on next cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Storage Node Lifecycle Worker stopped.");
    }

    private async Task<bool> TryRegisterAsync(CancellationToken ct)
    {
        var registrationKey = _options.RegistrationKey;
        if (string.IsNullOrWhiteSpace(registrationKey))
        {
            _logger.LogWarning("No RegistrationKey provided in options/environment (StorageNode:RegistrationKey). Cannot register automatically.");
            return false;
        }

        var masterBaseUrl = _options.MasterBaseUrl.TrimEnd('/');
        var registerUrl = $"{masterBaseUrl}/api/v1/nodes/register";

        var telemetry = _storageService.GetDiskTelemetry();
        var nodeName = !string.IsNullOrWhiteSpace(_options.NodeName)
            ? _options.NodeName.Trim()
            : Environment.MachineName;

        var requestBody = new RegisterNodeRequest
        {
            RegistrationKey = registrationKey.Trim(),
            Name = nodeName,
            BaseUrl = _options.BaseUrl,
            IsBehindNat = _options.ResolveIsBehindNat(),
            TotalSpaceBytes = telemetry.TotalBytes,
            AvailableSpaceBytes = telemetry.AvailableBytes
        };

        try
        {
            _logger.LogInformation("Attempting registration with Master at {Url}...", registerUrl);
            var response = await _httpClient.PostAsJsonAsync(registerUrl, requestBody, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Master rejected registration (HTTP {StatusCode}): {Error}", response.StatusCode, errorContent);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RegisterNodeResponse>(_jsonOptions, ct);
            if (result == null || result.NodeId == Guid.Empty || string.IsNullOrWhiteSpace(result.NodeSecret))
            {
                _logger.LogError("Master returned an empty or invalid registration payload.");
                return false;
            }

            var newState = new NodeState
            {
                NodeId = result.NodeId,
                NodeSecret = result.NodeSecret,
                Name = result.Name,
                BaseUrl = _options.BaseUrl,
                IsBehindNat = _options.ResolveIsBehindNat(),
                RegisteredAt = result.RegisteredAt
            };

            await _stateManager.SaveStateAsync(newState, ct);
            _logger.LogInformation("Successfully registered with Master! Assigned NodeId: {NodeId}", result.NodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Master at {Url} during registration.", registerUrl);
            return false;
        }
    }

    private async Task<int?> SendHeartbeatAsync(CancellationToken ct)
    {
        var nodeId = _stateManager.NodeId;
        var nodeSecret = _stateManager.NodeSecret;

        if (!nodeId.HasValue || string.IsNullOrWhiteSpace(nodeSecret))
        {
            _logger.LogWarning("Cannot send heartbeat: node is not configured.");
            return null;
        }

        var masterBaseUrl = _options.MasterBaseUrl.TrimEnd('/');
        var heartbeatUrl = $"{masterBaseUrl}/api/v1/nodes/heartbeat";

        var telemetry = _storageService.GetDiskTelemetry();
        var cpuLoad = _storageService.GetCpuUsage();

        var requestBody = new NodeHeartbeatRequest
        {
            NodeId = nodeId.Value,
            TotalSpaceBytes = telemetry.TotalBytes,
            AvailableSpaceBytes = telemetry.AvailableBytes,
            CpuLoad = cpuLoad,
            DiskStatus = telemetry.DiskStatus,
            BaseUrl = _options.BaseUrl,
            Status = "Online"
        };

        var unixTimestamp = new DateTimeOffset(_dateTimeProvider.UtcNow).ToUnixTimeSeconds();
        var signaturePayload = $"{nodeId.Value}:{unixTimestamp}";
        var signature = _hmacValidator.ComputeHmacSha256(signaturePayload, nodeSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, heartbeatUrl);
        request.Headers.Add("X-Node-Id", nodeId.Value.ToString());
        request.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        request.Headers.Add("X-Node-Signature", signature);
        request.Content = JsonContent.Create(requestBody);

        _logger.LogDebug("Sending heartbeat to Master {Url} (Disk: {Avail}/{Total})",
            heartbeatUrl, telemetry.AvailableBytes, telemetry.TotalBytes);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Master responded with HTTP {StatusCode} to heartbeat", response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<HeartbeatResponseDto>(_jsonOptions, ct);
        _logger.LogDebug("Heartbeat acknowledged by Master. Status: {Status}, NextInterval: {Interval}s",
            dto?.Status, dto?.NextHeartbeatIntervalSeconds);

        return dto?.NextHeartbeatIntervalSeconds;
    }
}
