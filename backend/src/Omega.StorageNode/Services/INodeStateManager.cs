using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;

namespace Omega.StorageNode.Services;

public interface INodeStateManager
{
    bool IsConfigured { get; }
    NodeState? CurrentState { get; }
    Guid? NodeId { get; }
    string? NodeSecret { get; }
    Task<NodeState?> LoadStateAsync(CancellationToken ct = default);
    Task SaveStateAsync(NodeState state, CancellationToken ct = default);
}

public class NodeStateManager : INodeStateManager
{
    private readonly StorageNodeOptions _options;
    private readonly ILogger<NodeStateManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private NodeState? _currentState;

    public NodeStateManager(
        IOptions<StorageNodeOptions> options,
        ILogger<NodeStateManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        InitializeState();
    }

    public bool IsConfigured
    {
        get
        {
            lock (_lock)
            {
                return _currentState != null &&
                       _currentState.NodeId != Guid.Empty &&
                       !string.IsNullOrWhiteSpace(_currentState.NodeSecret);
            }
        }
    }

    public NodeState? CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public Guid? NodeId => CurrentState?.NodeId;
    public string? NodeSecret => CurrentState?.NodeSecret;

    private void InitializeState()
    {
        lock (_lock)
        {
            // 1. Direct configuration / environment variable override
            if (_options.NodeId.HasValue && !string.IsNullOrWhiteSpace(_options.NodeSecret))
            {
                _currentState = new NodeState
                {
                    NodeId = _options.NodeId.Value,
                    NodeSecret = _options.NodeSecret.Trim(),
                    Name = _options.NodeName,
                    BaseUrl = _options.BaseUrl,
                    IsBehindNat = _options.ResolveIsBehindNat(),
                    RegisteredAt = DateTime.UtcNow
                };
                _logger.LogInformation("Loaded node state from configuration/environment override for node {NodeId}", _currentState.NodeId);
                return;
            }

            // 2. Load from state file in storage volume
            try
            {
                var statePath = GetStateFilePath();
                if (File.Exists(statePath))
                {
                    var json = File.ReadAllText(statePath);
                    var loaded = JsonSerializer.Deserialize<NodeState>(json, _jsonOptions);
                    if (loaded != null && loaded.NodeId != Guid.Empty && !string.IsNullOrWhiteSpace(loaded.NodeSecret))
                    {
                        // Allow environment variables to dynamically update BaseUrl/Nat and Name
                        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
                        {
                            loaded.BaseUrl = _options.BaseUrl;
                            loaded.IsBehindNat = _options.ResolveIsBehindNat();
                        }
                        if (!string.IsNullOrWhiteSpace(_options.NodeName))
                        {
                            loaded.Name = _options.NodeName;
                        }

                        _currentState = loaded;
                        _logger.LogInformation("Successfully loaded node state from {Path} for node {NodeId}", statePath, _currentState.NodeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read node state file. A fresh registration may be required.");
            }
        }
    }

    public Task<NodeState?> LoadStateAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_currentState != null)
            {
                return Task.FromResult<NodeState?>(_currentState);
            }
        }

        InitializeState();
        return Task.FromResult(CurrentState);
    }

    public async Task SaveStateAsync(NodeState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _fileLock.WaitAsync(ct);
        try
        {
            var statePath = GetStateFilePath();
            var dir = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = statePath + ".tmp";
            var json = JsonSerializer.Serialize(state, _jsonOptions);

            await File.WriteAllTextAsync(tempPath, json, ct);

            File.Move(tempPath, statePath, overwrite: true);

            lock (_lock)
            {
                _currentState = state;
            }

            _logger.LogInformation("Saved node state to {Path} for node {NodeId}", statePath, state.NodeId);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string GetStateFilePath()
    {
        var root = Path.GetFullPath(_options.StorageRootPath);
        return Path.Combine(root, _options.StateFileName);
    }
}
