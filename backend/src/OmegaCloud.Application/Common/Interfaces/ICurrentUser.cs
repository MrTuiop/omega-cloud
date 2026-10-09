namespace OmegaCloud.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Username { get; }
    Guid? SessionId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
