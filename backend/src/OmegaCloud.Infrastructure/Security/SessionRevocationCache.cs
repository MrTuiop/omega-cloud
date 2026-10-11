using System.Collections.Concurrent;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Infrastructure.Security;

public class SessionRevocationCache : ISessionRevocationCache
{
    private readonly ConcurrentDictionary<Guid, byte> _revokedSessionIds = new();

    public void Revoke(Guid sessionId)
    {
        _revokedSessionIds.TryAdd(sessionId, 0);
    }

    public bool IsRevoked(Guid sessionId)
    {
        return _revokedSessionIds.ContainsKey(sessionId);
    }
}
