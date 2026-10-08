using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("UploadSessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired();

        builder.Property(s => s.TargetFolderId);

        builder.Property(s => s.StorageNodeId)
            .IsRequired();

        builder.Property(s => s.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.TotalSizeBytes)
            .IsRequired();

        builder.Property(s => s.ChunkSizeBytes)
            .IsRequired();

        builder.Property(s => s.TotalChunks)
            .IsRequired();

        builder.Property(s => s.ExpectedSha256)
            .HasMaxLength(64);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.TempStoragePath)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.ExpiresAt)
            .IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.ExpiresAt);

        builder.HasOne(s => s.User)
            .WithMany(u => u.UploadSessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.TargetFolder)
            .WithMany()
            .HasForeignKey(s => s.TargetFolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.StorageNode)
            .WithMany(n => n.UploadSessions)
            .HasForeignKey(s => s.StorageNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
