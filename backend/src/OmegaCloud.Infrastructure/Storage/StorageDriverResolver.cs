using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Infrastructure.Storage;

public class StorageDriverResolver : IStorageDriverResolver
{
    private readonly LocalDiskStorageDriver _localDiskStorageDriver;
    private readonly IRemoteNodeGateway _remoteNodeGateway;
    private readonly RemoteNodeStorageDriver _defaultRemoteDriver;

    public StorageDriverResolver(LocalDiskStorageDriver localDiskStorageDriver)
        : this(localDiskStorageDriver, null)
    {
    }

    public StorageDriverResolver(
        LocalDiskStorageDriver localDiskStorageDriver,
        IRemoteNodeGateway? remoteNodeGateway)
    {
        _localDiskStorageDriver = localDiskStorageDriver;
        _remoteNodeGateway = remoteNodeGateway!;
        _defaultRemoteDriver = remoteNodeGateway != null ? new RemoteNodeStorageDriver(remoteNodeGateway) : null!;
    }

    public IStorageDriver GetDriver(StorageNodeType nodeType)
    {
        return nodeType switch
        {
            StorageNodeType.LocalDisk => _localDiskStorageDriver,
            StorageNodeType.RemoteDaemon => _defaultRemoteDriver,
            _ => _localDiskStorageDriver
        };
    }

    public IStorageDriver GetDriverForNode(StorageNode node)
    {
        if (node.Type == StorageNodeType.RemoteDaemon)
        {
            return new RemoteNodeStorageDriver(node, _remoteNodeGateway);
        }

        return _localDiskStorageDriver;
    }
}
