using System.Text.Json.Serialization;

namespace OmegaCloud.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StorageNodeStatus
{
    Online,
    Degraded,
    Offline,
    Maintenance
}
