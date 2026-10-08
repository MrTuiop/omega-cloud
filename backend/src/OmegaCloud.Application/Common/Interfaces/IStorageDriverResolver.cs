using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IStorageDriverResolver
{
    IStorageDriver GetDriver(StorageNodeType nodeType);
    IStorageDriver GetDriverForNode(StorageNode node);
}
