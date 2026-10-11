using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class FileItemConfiguration : IEntityTypeConfiguration<FileItem>
{
    public void Configure(EntityTypeBuilder<FileItem> builder)
    {
        builder.ToTable("FileItems");

        builder.HasKey(fi => fi.Id);

        builder.Property(fi => fi.UserId)
            .IsRequired();

        builder.Property(fi => fi.FolderId);

        builder.Property(fi => fi.StorageBlobId)
            .IsRequired();

        builder.Property(fi => fi.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(fi => fi.Extension)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(fi => fi.MimeType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(fi => fi.SizeBytes)
            .IsRequired();

        builder.Property(fi => fi.IsShared)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(fi => fi.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(fi => fi.DeletedAt);

        builder.Property(fi => fi.IsFavorite)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(fi => fi.CreatedAt)
            .IsRequired();

        builder.Property(fi => fi.UpdatedAt)
            .IsRequired();

        builder.HasIndex(fi => fi.UserId);
        builder.HasIndex(fi => fi.FolderId);
        builder.HasIndex(fi => fi.StorageBlobId);
        builder.HasIndex(fi => fi.IsDeleted);
        builder.HasIndex(fi => fi.IsShared);
        builder.HasIndex(fi => new { fi.IsShared, fi.FolderId, fi.IsDeleted });
        builder.HasIndex(fi => new { fi.UserId, fi.FolderId, fi.Name, fi.IsDeleted });

        builder.HasOne(fi => fi.User)
            .WithMany(u => u.FileItems)
            .HasForeignKey(fi => fi.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(fi => fi.Folder)
            .WithMany(f => f.FileItems)
            .HasForeignKey(fi => fi.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(fi => fi.StorageBlob)
            .WithMany(b => b.FileItems)
            .HasForeignKey(fi => fi.StorageBlobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
