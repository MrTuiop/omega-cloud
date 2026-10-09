using OmegaCloud.Application.DTOs.Nodes;

namespace OmegaCloud.Application.Services;

public interface INodeService
{
    Task<RegisterNodeResponse> RegisterNodeAsync(RegisterNodeRequest request, CancellationToken ct = default);
    Task<HeartbeatResponseDto> ProcessHeartbeatAsync(
        NodeHeartbeatRequest request,
        string? nodeIdHeader,
        string? signatureHeader,
        string? timestampHeader,
        string? secretHeader,
        string? authHeader,
        CancellationToken ct = default);
    Task<IReadOnlyList<StorageNodeDto>> GetAllNodesAsync(CancellationToken ct = default);
    Task<StorageNodeDto> GetNodeByIdAsync(Guid id, CancellationToken ct = default);
    Task<NodeRegistrationTokenDto> GenerateRegistrationTokenAsync(GenerateNodeTokenRequest request, CancellationToken ct = default);
    Task<bool> DeleteOrArchiveNodeAsync(Guid id, CancellationToken ct = default);
    Task<OfflineNodesCheckResultDto> CheckOfflineNodesAsync(TimeSpan offlineThreshold, TimeSpan degradedThreshold, CancellationToken ct = default);
    Task<NodePingResultDto> PingNodeAsync(Guid id, CancellationToken ct = default);
    Task<StorageNodeDto> UpdateNodeAsync(Guid id, UpdateStorageNodeRequest request, CancellationToken ct = default);
}
