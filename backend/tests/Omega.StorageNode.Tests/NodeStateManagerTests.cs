using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Tests;

public class NodeStateManagerTests : IDisposable
{
    private readonly string _testDir;

    public NodeStateManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "StorageNodeStateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void InitializeState_ConfiguredViaOptions_StateLoadedImmediately()
    {
        var options = new StorageNodeOptions
        {
            StorageRootPath = _testDir,
            NodeId = Guid.NewGuid(),
            NodeSecret = "secret_from_env_123",
            NodeName = "Configured-Node",
            BaseUrl = "https://node.example.com",
            IsBehindNat = false
        };

        var manager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);

        Assert.True(manager.IsConfigured);
        Assert.Equal(options.NodeId, manager.NodeId);
        Assert.Equal(options.NodeSecret, manager.NodeSecret);
    }

    [Fact]
    public void InitializeState_EmptyOptions_NotConfigured()
    {
        var options = new StorageNodeOptions
        {
            StorageRootPath = _testDir,
            NodeId = null,
            NodeSecret = null
        };

        var manager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);

        Assert.False(manager.IsConfigured);
        Assert.Null(manager.NodeId);
        Assert.Null(manager.NodeSecret);
    }

    [Fact]
    public async Task SaveStateAsync_SavesAndReloadsStateFile()
    {
        var options = new StorageNodeOptions
        {
            StorageRootPath = _testDir,
            StateFileName = "node-state.json"
        };

        var manager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);
        Assert.False(manager.IsConfigured);

        var stateToSave = new NodeState
        {
            NodeId = Guid.NewGuid(),
            NodeSecret = "saved_secret_hex",
            Name = "Saved-Node-1",
            BaseUrl = "https://saved.local:5001",
            IsBehindNat = false,
            RegisteredAt = DateTime.UtcNow
        };

        await manager.SaveStateAsync(stateToSave);

        Assert.True(manager.IsConfigured);
        Assert.Equal(stateToSave.NodeId, manager.NodeId);
        Assert.Equal(stateToSave.NodeSecret, manager.NodeSecret);

        // Verify file on disk
        var stateFilePath = Path.Combine(_testDir, "node-state.json");
        Assert.True(File.Exists(stateFilePath));

        // Create a new manager instance and verify it reads the persisted file
        var newManager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);
        Assert.True(newManager.IsConfigured);
        Assert.Equal(stateToSave.NodeId, newManager.NodeId);
        Assert.Equal(stateToSave.NodeSecret, newManager.NodeSecret);
    }

    [Fact]
    public void CorruptedStateFile_DoesNotThrow_LeavesUnconfigured()
    {
        var options = new StorageNodeOptions
        {
            StorageRootPath = _testDir,
            StateFileName = "node-state.json"
        };

        var stateFilePath = Path.Combine(_testDir, "node-state.json");
        File.WriteAllText(stateFilePath, "{ invalid json content!@#$ }");

        var manager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);

        Assert.False(manager.IsConfigured);
    }
}
