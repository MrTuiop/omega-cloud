using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;
using Omega.StorageNode.Services;
using Omega.StorageNode.Workers;

namespace Omega.StorageNode.Tests;

public class NodeLifecycleWorkerTests : IDisposable
{
    private class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 9, 14, 0, 0, DateTimeKind.Utc);
    }

    private class MockMasterHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRegisterRequest { get; private set; }
        public RegisterNodeRequest? LastRegisterPayload { get; private set; }
        public HttpRequestMessage? LastHeartbeatRequest { get; private set; }
        public NodeHeartbeatRequest? LastHeartbeatPayload { get; private set; }

        public Guid AssignedNodeId { get; set; } = Guid.NewGuid();
        public string AssignedNodeSecret { get; set; } = "assigned_secret_hex_987654";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (uri.EndsWith("/api/v1/nodes/register"))
            {
                LastRegisterRequest = request;
                if (request.Content != null)
                {
                    LastRegisterPayload = await request.Content.ReadFromJsonAsync<RegisterNodeRequest>(cancellationToken: cancellationToken);
                }

                var responsePayload = new RegisterNodeResponse
                {
                    NodeId = AssignedNodeId,
                    NodeSecret = AssignedNodeSecret,
                    Name = LastRegisterPayload?.Name ?? "Registered-Node",
                    Status = "Online",
                    RegisteredAt = DateTime.UtcNow
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(responsePayload)
                };
            }

            if (uri.EndsWith("/api/v1/nodes/heartbeat"))
            {
                LastHeartbeatRequest = request;
                if (request.Content != null)
                {
                    LastHeartbeatPayload = await request.Content.ReadFromJsonAsync<NodeHeartbeatRequest>(cancellationToken: cancellationToken);
                }

                var responsePayload = new HeartbeatResponseDto
                {
                    NodeId = AssignedNodeId,
                    Status = "Online",
                    AcknowledgedAt = DateTime.UtcNow,
                    NextHeartbeatIntervalSeconds = 15
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(responsePayload)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private readonly string _testDir;

    public NodeLifecycleWorkerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NodeLifecycleWorkerTest_" + Guid.NewGuid().ToString("N"));
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
    public async Task Worker_UnconfiguredNode_PerformsRegistrationAndTransitionsToHeartbeat()
    {
        var handler = new MockMasterHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        var options = new StorageNodeOptions
        {
            MasterBaseUrl = "http://master.local:5000",
            RegistrationKey = "node_reg_valid_key_123",
            NodeName = "NAS-Worker-Test",
            BaseUrl = "https://nas-worker.local:5001",
            IsBehindNat = false,
            StorageRootPath = _testDir,
            HeartbeatIntervalSeconds = 1 // short for testing
        };

        var timeProvider = new TestDateTimeProvider();
        var stateManager = new NodeStateManager(Options.Create(options), NullLogger<NodeStateManager>.Instance);
        var hmacValidator = new NodeHmacValidator(timeProvider, NullLogger<NodeHmacValidator>.Instance);
        var storageService = new StorageService(_testDir, NullLogger<StorageService>.Instance);

        Assert.False(stateManager.IsConfigured);

        var worker = new NodeLifecycleWorker(
            httpClient,
            stateManager,
            hmacValidator,
            storageService,
            timeProvider,
            Options.Create(options),
            NullLogger<NodeLifecycleWorker>.Instance);

        using var cts = new CancellationTokenSource();

        // Start worker and allow registration and initial heartbeat to execute
        var runTask = worker.StartAsync(cts.Token);

        // Wait a short time for registration and heartbeat
        for (int i = 0; i < 20; i++)
        {
            if (stateManager.IsConfigured && handler.LastHeartbeatRequest != null)
            {
                break;
            }
            await Task.Delay(100);
        }

        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        // Verify registration
        Assert.NotNull(handler.LastRegisterRequest);
        Assert.NotNull(handler.LastRegisterPayload);
        Assert.Equal("node_reg_valid_key_123", handler.LastRegisterPayload.RegistrationKey);
        Assert.Equal("NAS-Worker-Test", handler.LastRegisterPayload.Name);

        // Verify state is saved
        Assert.True(stateManager.IsConfigured);
        Assert.Equal(handler.AssignedNodeId, stateManager.NodeId);
        Assert.Equal(handler.AssignedNodeSecret, stateManager.NodeSecret);

        // Verify state file persisted
        var stateFile = Path.Combine(_testDir, "node-state.json");
        Assert.True(File.Exists(stateFile));

        // Verify heartbeat request
        Assert.NotNull(handler.LastHeartbeatRequest);
        Assert.True(handler.LastHeartbeatRequest.Headers.Contains("X-Node-Id"));
        Assert.True(handler.LastHeartbeatRequest.Headers.Contains("X-Node-Timestamp"));
        Assert.True(handler.LastHeartbeatRequest.Headers.Contains("X-Node-Signature"));

        var headerNodeId = handler.LastHeartbeatRequest.Headers.GetValues("X-Node-Id").First();
        var headerTime = handler.LastHeartbeatRequest.Headers.GetValues("X-Node-Timestamp").First();
        var headerSig = handler.LastHeartbeatRequest.Headers.GetValues("X-Node-Signature").First();

        Assert.Equal(handler.AssignedNodeId.ToString(), headerNodeId);

        // Verify HMAC signature
        var expectedSig = hmacValidator.ComputeHmacSha256($"{handler.AssignedNodeId}:{headerTime}", handler.AssignedNodeSecret);
        Assert.Equal(expectedSig, headerSig);
    }
}
