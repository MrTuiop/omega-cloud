using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class NodeService : INodeService
{
    private readonly IAppDbContext _dbContext;
    private readonly IHmacSigner _hmacSigner;
    private readonly IRemoteNodeGateway _remoteNodeGateway;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<NodeService> _logger;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditService? _auditService;

    public NodeService(
        IAppDbContext dbContext,
        IHmacSigner hmacSigner,
        IRemoteNodeGateway remoteNodeGateway,
        IDateTimeProvider dateTimeProvider,
        ILogger<NodeService> logger,
        IRealtimeNotifier? notifier = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _hmacSigner = hmacSigner;
        _remoteNodeGateway = remoteNodeGateway;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
        _auditService = auditService;
    }

    public async Task<RegisterNodeResponse> RegisterNodeAsync(RegisterNodeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RegistrationKey))
        {
            throw new ValidationException("Registration key is required.");
        }

        if (request.TotalSpaceBytes < 0 || request.AvailableSpaceBytes < 0)
        {
            throw new ValidationException("Disk space values cannot be negative.");
        }

        if (request.TotalSpaceBytes > 0 && request.AvailableSpaceBytes > request.TotalSpaceBytes)
        {
            throw new ValidationException("Available space cannot exceed total space.");
        }

        var key = request.RegistrationKey.Trim();
        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.RegistrationKey == key, ct);

        if (node == null)
        {
            throw new NotFoundException("Invalid or unknown registration key.");
        }

        if (!node.IsActive)
        {
            throw new ValidationException("This storage node has been deactivated.");
        }

        if (!string.IsNullOrWhiteSpace(node.NodeSecretHash))
        {
            throw new ValidationException("Registration key has already been used. Node is already registered.");
        }

        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var nodeSecret = Convert.ToHexString(secretBytes).ToLowerInvariant();

        node.NodeSecretHash = nodeSecret;
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            node.Name = request.Name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            node.BaseUrl = request.BaseUrl.Trim();
        }
        node.IsBehindNat = request.IsBehindNat;

        if (request.TotalSpaceBytes > 0)
        {
            node.TotalSpaceBytes = request.TotalSpaceBytes;
            if (node.MaxCapacityBytes == 0)
            {
                node.MaxCapacityBytes = request.TotalSpaceBytes;
            }
        }
        if (request.AvailableSpaceBytes >= 0)
        {
            node.AvailableSpaceBytes = request.AvailableSpaceBytes;
        }

        var now = _dateTimeProvider.UtcNow;
        node.Status = StorageNodeStatus.Online;
        node.LastHeartbeatAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await _notifier.NotifyNodeStatusChangedAsync(node.Id, node.Status.ToString(), node.LastHeartbeatAt, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "NodeRegistered",
                resource: "StorageNodes",
                resourceId: node.Id.ToString(),
                userId: null,
                username: null,
                ipAddress: null,
                details: new { nodeName = node.Name, type = node.Type.ToString(), isBehindNat = node.IsBehindNat, totalSpaceBytes = node.TotalSpaceBytes },
                ct: ct);
        }

        _logger.LogInformation("Successfully registered storage node {NodeId} ({NodeName})", node.Id, node.Name);

        return new RegisterNodeResponse
        {
            NodeId = node.Id,
            NodeSecret = nodeSecret,
            Name = node.Name,
            Status = node.Status.ToString(),
            RegisteredAt = now
        };
    }

    public async Task<HeartbeatResponseDto> ProcessHeartbeatAsync(
        NodeHeartbeatRequest request,
        string? nodeIdHeader,
        string? signatureHeader,
        string? timestampHeader,
        string? secretHeader,
        string? authHeader,
        CancellationToken ct = default)
    {
        Guid nodeId;
        if (!string.IsNullOrWhiteSpace(nodeIdHeader) && Guid.TryParse(nodeIdHeader, out var parsedFromHeader))
        {
            nodeId = parsedFromHeader;
            if (request.NodeId.HasValue && request.NodeId.Value != nodeId)
            {
                throw new ValidationException($"Node ID in header '{nodeId}' does not match Node ID in body '{request.NodeId.Value}'.");
            }
        }
        else if (request.NodeId.HasValue)
        {
            nodeId = request.NodeId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(authHeader) && TryParseNodeIdFromAuthHeader(authHeader, out var parsedFromAuth))
        {
            nodeId = parsedFromAuth;
            if (request.NodeId.HasValue && request.NodeId.Value != nodeId)
            {
                throw new ValidationException($"Node ID in auth header '{nodeId}' does not match Node ID in body '{request.NodeId.Value}'.");
            }
        }
        else
        {
            throw new UnauthorizedException("Storage node ID was not provided in request headers or body.");
        }

        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node == null)
        {
            throw new NotFoundException($"Storage node '{nodeId}' was not found.");
        }

        if (!node.IsActive)
        {
            throw new UnauthorizedException("Storage node is deactivated.");
        }

        if (string.IsNullOrWhiteSpace(node.NodeSecretHash))
        {
            throw new UnauthorizedException("Storage node is not fully registered yet.");
        }

        // Authenticate the node via HMAC signature or secret token
        var isAuthenticated = AuthenticateHeartbeat(
            node,
            signatureHeader,
            timestampHeader,
            secretHeader,
            authHeader);

        if (!isAuthenticated)
        {
            _logger.LogWarning("Failed authentication attempt for storage node {NodeId}", node.Id);
            throw new UnauthorizedException("Storage node authentication failed. Invalid HMAC signature or secret.");
        }

        var now = _dateTimeProvider.UtcNow;
        node.LastHeartbeatAt = now;

        if (request.TotalSpaceBytes > 0)
        {
            node.TotalSpaceBytes = request.TotalSpaceBytes;
            if (node.MaxCapacityBytes == 0)
            {
                node.MaxCapacityBytes = request.TotalSpaceBytes;
            }
        }
        if (request.AvailableSpaceBytes >= 0)
        {
            node.AvailableSpaceBytes = request.AvailableSpaceBytes;
        }
        if (request.CpuLoad.HasValue)
        {
            node.CpuLoad = Math.Clamp(request.CpuLoad.Value, 0.0, 100.0);
        }
        if (!string.IsNullOrWhiteSpace(request.DiskStatus))
        {
            node.DiskStatus = request.DiskStatus;
        }
        if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            node.BaseUrl = request.BaseUrl.Trim();
        }

        var previousStatus = node.Status;
        if (request.Status.HasValue)
        {
            node.Status = request.Status.Value;
        }
        else if (node.Status == StorageNodeStatus.Offline || node.Status == StorageNodeStatus.Degraded)
        {
            node.Status = StorageNodeStatus.Online;
        }

        await _dbContext.SaveChangesAsync(ct);

        if (previousStatus != node.Status)
        {
            await _notifier.NotifyNodeStatusChangedAsync(node.Id, node.Status.ToString(), node.LastHeartbeatAt, ct);
        }

        _logger.LogDebug("Received heartbeat from node {NodeId} ({Name}) - Status: {Status}", node.Id, node.Name, node.Status);

        return new HeartbeatResponseDto
        {
            NodeId = node.Id,
            Status = node.Status.ToString(),
            AcknowledgedAt = now,
            NextHeartbeatIntervalSeconds = 20
        };
    }

    public async Task<IReadOnlyList<StorageNodeDto>> GetAllNodesAsync(CancellationToken ct = default)
    {
        var now = _dateTimeProvider.UtcNow;
        var nodes = await _dbContext.StorageNodes
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

        return nodes.Select(n => MapToDto(n, now)).ToList();
    }

    public async Task<StorageNodeDto> GetNodeByIdAsync(Guid id, CancellationToken ct = default)
    {
        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"Storage node '{id}' was not found.");

        return MapToDto(node, _dateTimeProvider.UtcNow);
    }

    public async Task<NodeRegistrationTokenDto> GenerateRegistrationTokenAsync(GenerateNodeTokenRequest request, CancellationToken ct = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(24);
        var regKey = "node_reg_" + Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var nodeName = !string.IsNullOrWhiteSpace(request.Name)
            ? request.Name.Trim()
            : $"Remote-Node-{Guid.NewGuid():N}"[..18];

        var now = _dateTimeProvider.UtcNow;
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = nodeName,
            RegistrationKey = regKey,
            NodeSecretHash = string.Empty,
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = request.BaseUrl?.Trim(),
            IsBehindNat = request.IsBehindNat,
            Status = StorageNodeStatus.Offline,
            TotalSpaceBytes = 0,
            AvailableSpaceBytes = 0,
            LastHeartbeatAt = now,
            CreatedAt = now,
            IsActive = true
        };

        _dbContext.StorageNodes.Add(node);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Generated registration token for storage node {NodeId} ({Name})", node.Id, node.Name);

        return new NodeRegistrationTokenDto
        {
            NodeId = node.Id,
            Name = node.Name,
            RegistrationKey = regKey,
            CreatedAt = now
        };
    }

    public async Task<bool> DeleteOrArchiveNodeAsync(Guid id, CancellationToken ct = default)
    {
        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"Storage node '{id}' was not found.");

        if (node.Type == StorageNodeType.LocalDisk)
        {
            throw new ValidationException("Master local storage node cannot be deleted.");
        }

        var hasBlobs = await _dbContext.StorageBlobs.AnyAsync(b => b.StorageNodeId == id, ct);
        var hasSessions = await _dbContext.UploadSessions.AnyAsync(s => s.StorageNodeId == id, ct);

        if (hasBlobs || hasSessions)
        {
            // Archive and disable the node so blob relationships remain valid
            node.Status = StorageNodeStatus.Offline;
            node.IsActive = false;
            _logger.LogInformation("Node {NodeId} has existing blobs or sessions. Archiving node instead of hard delete.", node.Id);
        }
        else
        {
            _dbContext.StorageNodes.Remove(node);
            _logger.LogInformation("Removed storage node {NodeId} from registry.", node.Id);
        }

        await _dbContext.SaveChangesAsync(ct);
        await _notifier.NotifyNodeStatusChangedAsync(id, StorageNodeStatus.Offline.ToString(), _dateTimeProvider.UtcNow, ct);
        return true;
    }

    public async Task<OfflineNodesCheckResultDto> CheckOfflineNodesAsync(
        TimeSpan offlineThreshold,
        TimeSpan degradedThreshold,
        CancellationToken ct = default)
    {
        var now = _dateTimeProvider.UtcNow;
        var nodes = await _dbContext.StorageNodes
            .Where(n => n.Type == StorageNodeType.RemoteDaemon && n.IsActive)
            .ToListAsync(ct);

        var result = new OfflineNodesCheckResultDto
        {
            CheckedNodesCount = nodes.Count
        };

        bool hasChanges = false;

        foreach (var node in nodes)
        {
            // Do not automatically change status of nodes under intentional maintenance
            if (node.Status == StorageNodeStatus.Maintenance)
            {
                continue;
            }

            var elapsed = now - node.LastHeartbeatAt;

            if (elapsed > offlineThreshold)
            {
                if (node.Status != StorageNodeStatus.Offline)
                {
                    _logger.LogWarning("Storage node {NodeId} ({Name}) missed heartbeats for {ElapsedSeconds:F0}s (threshold: {Threshold}s). Marking OFFLINE.",
                        node.Id, node.Name, elapsed.TotalSeconds, offlineThreshold.TotalSeconds);

                    node.Status = StorageNodeStatus.Offline;
                    result.MarkedOfflineCount++;
                    result.AffectedNodeIds.Add(node.Id);
                    hasChanges = true;
                }
            }
            else if (elapsed > degradedThreshold)
            {
                if (node.Status == StorageNodeStatus.Online)
                {
                    _logger.LogWarning("Storage node {NodeId} ({Name}) missed heartbeats for {ElapsedSeconds:F0}s (threshold: {Threshold}s). Marking DEGRADED.",
                        node.Id, node.Name, elapsed.TotalSeconds, degradedThreshold.TotalSeconds);

                    node.Status = StorageNodeStatus.Degraded;
                    result.MarkedDegradedCount++;
                    result.AffectedNodeIds.Add(node.Id);
                    hasChanges = true;
                }
            }
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(ct);

            foreach (var node in nodes.Where(n => result.AffectedNodeIds.Contains(n.Id)))
            {
                await _notifier.NotifyNodeStatusChangedAsync(node.Id, node.Status.ToString(), node.LastHeartbeatAt, ct);

                if (_auditService != null && node.Status == StorageNodeStatus.Offline)
                {
                    await _auditService.LogAsync(
                        action: "NodeWentOffline",
                        resource: "StorageNodes",
                        resourceId: node.Id.ToString(),
                        userId: null,
                        username: null,
                        ipAddress: null,
                        details: new { nodeName = node.Name, lastHeartbeatAt = node.LastHeartbeatAt },
                        ct: ct);
                }
            }
        }

        return result;
    }

    public async Task<NodePingResultDto> PingNodeAsync(Guid id, CancellationToken ct = default)
    {
        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"Storage node '{id}' was not found.");

        if (node.Type == StorageNodeType.LocalDisk)
        {
            node.LatencyMs = 0;
            node.LastHeartbeatAt = _dateTimeProvider.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            return new NodePingResultDto
            {
                NodeId = node.Id,
                IsReachable = true,
                LatencyMs = 0,
                Status = node.Status.ToString()
            };
        }

        var latency = await _remoteNodeGateway.MeasureLatencyMsAsync(node, ct);

        var previousStatus = node.Status;
        if (latency.HasValue)
        {
            node.LatencyMs = latency.Value;
            if (node.Status == StorageNodeStatus.Offline || node.Status == StorageNodeStatus.Degraded)
            {
                node.Status = StorageNodeStatus.Online;
            }
            node.LastHeartbeatAt = _dateTimeProvider.UtcNow;
        }
        else
        {
            node.LatencyMs = null;
        }

        await _dbContext.SaveChangesAsync(ct);

        if (previousStatus != node.Status)
        {
            await _notifier.NotifyNodeStatusChangedAsync(node.Id, node.Status.ToString(), node.LastHeartbeatAt, ct);
        }

        return new NodePingResultDto
        {
            NodeId = node.Id,
            IsReachable = latency.HasValue,
            LatencyMs = latency,
            Status = node.Status.ToString()
        };
    }

    public async Task<StorageNodeDto> UpdateNodeAsync(Guid id, UpdateStorageNodeRequest request, CancellationToken ct = default)
    {
        var node = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException($"Storage node '{id}' was not found.");

        if (node.Type == StorageNodeType.LocalDisk)
        {
            if (request.Status.HasValue && request.Status.Value == StorageNodeStatus.Offline)
            {
                throw new ValidationException("Master local storage node cannot be marked offline.");
            }
            if (request.IsActive.HasValue && !request.IsActive.Value)
            {
                throw new ValidationException("Master local storage node cannot be deactivated.");
            }
        }

        var previousStatus = node.Status;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            node.Name = request.Name.Trim();
        }

        if (request.BaseUrl != null)
        {
            node.BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim();
        }

        if (request.IsBehindNat.HasValue)
        {
            node.IsBehindNat = request.IsBehindNat.Value;
        }

        if (request.Status.HasValue)
        {
            node.Status = request.Status.Value;
        }

        if (request.IsActive.HasValue)
        {
            node.IsActive = request.IsActive.Value;
        }

        if (request.MaxCapacityBytes.HasValue)
        {
            if (request.MaxCapacityBytes.Value < 0)
            {
                throw new ValidationException("Max capacity cannot be negative.");
            }
            node.MaxCapacityBytes = request.MaxCapacityBytes.Value;
        }

        if (request.MaxCapacityBytes.HasValue || request.IsActive.HasValue)
        {
            var otherActiveCapacity = await _dbContext.StorageNodes
                .Where(n => n.Id != node.Id && n.IsActive)
                .SumAsync(n => n.MaxCapacityBytes, ct);

            var totalActiveCapacity = otherActiveCapacity + (node.IsActive ? node.MaxCapacityBytes : 0L);

            var totalAllocatedQuota = await _dbContext.Users
                .SumAsync(u => u.AllocatedQuotaBytes, ct);

            if (totalAllocatedQuota > totalActiveCapacity)
            {
                throw new ValidationException(
                    $"Cannot update node capacity or active status. Total user quotas ({totalAllocatedQuota} bytes) would exceed active storage nodes capacity ({totalActiveCapacity} bytes).");
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        if (previousStatus != node.Status)
        {
            await _notifier.NotifyNodeStatusChangedAsync(node.Id, node.Status.ToString(), node.LastHeartbeatAt, ct);
        }

        _logger.LogInformation("Updated storage node {NodeId} ({Name}) - Status: {Status}, Active: {IsActive}",
            node.Id, node.Name, node.Status, node.IsActive);

        return MapToDto(node, _dateTimeProvider.UtcNow);
    }

    private bool AuthenticateHeartbeat(
        StorageNode node,
        string? signatureHeader,
        string? timestampHeader,
        string? secretHeader,
        string? authHeader)
    {
        // 1. Direct secret header check
        if (!string.IsNullOrWhiteSpace(secretHeader))
        {
            return ConstantTimeEquals(secretHeader.Trim(), node.NodeSecretHash);
        }

        // 2. Authorization header check (Bearer token or Node token)
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            var trimmed = authHeader.Trim();
            if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = trimmed["Bearer ".Length..].Trim();
                if (ConstantTimeEquals(token, node.NodeSecretHash))
                {
                    return true;
                }
            }
            else if (trimmed.StartsWith("Node ", StringComparison.OrdinalIgnoreCase))
            {
                var credentials = trimmed["Node ".Length..].Trim();
                var parts = credentials.Split(':', 2);
                if (parts.Length == 2)
                {
                    var tokenOrSig = parts[1].Trim();
                    if (ConstantTimeEquals(tokenOrSig, node.NodeSecretHash))
                    {
                        return true;
                    }
                }
            }
        }

        // 3. HMAC signature header check with strict clock skew verification
        if (!string.IsNullOrWhiteSpace(signatureHeader) && !string.IsNullOrWhiteSpace(timestampHeader))
        {
            DateTimeOffset timestampDt = default;
            bool validTimestamp = false;

            if (long.TryParse(timestampHeader.Trim(), out var unixSeconds))
            {
                timestampDt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                validTimestamp = true;
            }
            else if (DateTimeOffset.TryParse(timestampHeader.Trim(), out var parsedIso))
            {
                timestampDt = parsedIso;
                validTimestamp = true;
            }

            if (!validTimestamp)
            {
                _logger.LogWarning("Rejecting HMAC heartbeat for node {NodeId}: invalid timestamp format '{Header}'", node.Id, timestampHeader);
                return false;
            }

            var diff = Math.Abs((_dateTimeProvider.UtcNow - timestampDt).TotalSeconds);
            if (diff > 300) // 5 minutes max clock skew
            {
                _logger.LogWarning("Rejecting HMAC heartbeat for node {NodeId}: timestamp skew is {Diff} seconds", node.Id, diff);
                return false;
            }

            // Verify standard signature payload: "{nodeId}:{unixSeconds}"
            if (long.TryParse(timestampHeader.Trim(), out var uSec))
            {
                var payload1 = $"{node.Id}:{uSec}";
                if (_hmacSigner.VerifyHmacSha256(payload1, node.NodeSecretHash, signatureHeader.Trim()))
                {
                    return true;
                }
            }

            // Also support ISO timestamp string payload
            var payload2 = $"{node.Id}:{timestampHeader.Trim()}";
            if (_hmacSigner.VerifyHmacSha256(payload2, node.NodeSecretHash, signatureHeader.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseNodeIdFromAuthHeader(string authHeader, out Guid nodeId)
    {
        nodeId = Guid.Empty;
        var trimmed = authHeader.Trim();
        if (trimmed.StartsWith("Node ", StringComparison.OrdinalIgnoreCase))
        {
            var credentials = trimmed["Node ".Length..].Trim();
            var parts = credentials.Split(':', 2);
            if (parts.Length >= 1 && Guid.TryParse(parts[0], out nodeId))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    private static StorageNodeDto MapToDto(StorageNode n, DateTime now)
    {
        var elapsed = (now - n.LastHeartbeatAt).TotalSeconds;
        var isOnline = n.Status == StorageNodeStatus.Online && elapsed <= 60;

        return new StorageNodeDto
        {
            Id = n.Id,
            Name = n.Name,
            Type = n.Type.ToString(),
            BaseUrl = n.BaseUrl,
            IsBehindNat = n.IsBehindNat,
            Status = n.Status.ToString(),
            TotalSpaceBytes = n.TotalSpaceBytes,
            AvailableSpaceBytes = n.AvailableSpaceBytes,
            MaxCapacityBytes = n.MaxCapacityBytes,
            CpuLoad = n.CpuLoad,
            DiskStatus = n.DiskStatus,
            LatencyMs = n.LatencyMs,
            LastHeartbeatAt = n.LastHeartbeatAt,
            SecondsSinceLastHeartbeat = Math.Max(0, Math.Round(elapsed, 1)),
            IsOnline = isOnline,
            IsActive = n.IsActive,
            CreatedAt = n.CreatedAt
        };
    }
}
