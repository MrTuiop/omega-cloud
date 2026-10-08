using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> FileItems => Set<FileItem>();
    public DbSet<StorageNode> StorageNodes => Set<StorageNode>();
    public DbSet<StorageBlob> StorageBlobs => Set<StorageBlob>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<UploadChunk> UploadChunks => Set<UploadChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Folder>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<FileItem>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
