using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserId)
            .IsRequired();

        builder.Property(f => f.ParentFolderId);

        builder.Property(f => f.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.Color)
            .HasMaxLength(32);

        builder.Property(f => f.IsShared)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(f => f.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(f => f.DeletedAt);

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .IsRequired();

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.ParentFolderId);
        builder.HasIndex(f => f.IsDeleted);
        builder.HasIndex(f => f.IsShared);
        builder.HasIndex(f => new { f.IsShared, f.ParentFolderId, f.IsDeleted });
        builder.HasIndex(f => new { f.UserId, f.ParentFolderId, f.Name, f.IsDeleted });

        builder.HasOne(f => f.User)
            .WithMany(u => u.Folders)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.ParentFolder)
            .WithMany(f => f.SubFolders)
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.FileItems)
            .WithOne(fi => fi.Folder)
            .HasForeignKey(fi => fi.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
