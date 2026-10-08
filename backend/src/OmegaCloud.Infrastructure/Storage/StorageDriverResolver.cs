using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Infrastructure.Storage;

public class StorageDriverResolver : IStorageDriverResolver
{
    private readonly LocalDiskStorageDriver _localDiskStorageDriver;

    public StorageDriverResolver(LocalDiskStorageDriver localDiskStorageDriver)
    {
        _localDiskStorageDriver = localDiskStorageDriver;
    }

    public IStorageDriver GetDriver(StorageNodeType nodeType)
    {
        return nodeType switch
        {
            StorageNodeType.LocalDisk => _localDiskStorageDriver,
            _ => _localDiskStorageDriver // In future milestones, RemoteNodeStorageDriver will be resolved here
        };
    }

    public IStorageDriver GetDriverForNode(StorageNode node)
    {
        return GetDriver(node.Type);
    }
}
