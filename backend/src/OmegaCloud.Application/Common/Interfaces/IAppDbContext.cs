using Microsoft.EntityFrameworkCore;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserSession> UserSessions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
