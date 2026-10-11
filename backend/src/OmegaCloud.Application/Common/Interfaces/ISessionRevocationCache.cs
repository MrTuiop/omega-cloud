namespace OmegaCloud.Application.Common.Interfaces;

public interface ISessionRevocationCache
{
    void Revoke(Guid sessionId);
    bool IsRevoked(Guid sessionId);
}
