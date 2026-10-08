using Microsoft.EntityFrameworkCore;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<Folder> Folders { get; }
    DbSet<FileItem> FileItems { get; }
    DbSet<StorageNode> StorageNodes { get; }
    DbSet<StorageBlob> StorageBlobs { get; }
    DbSet<UploadSession> UploadSessions { get; }
    DbSet<UploadChunk> UploadChunks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
