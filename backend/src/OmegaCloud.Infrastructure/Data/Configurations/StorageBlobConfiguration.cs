using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class StorageBlobConfiguration : IEntityTypeConfiguration<StorageBlob>
{
    public void Configure(EntityTypeBuilder<StorageBlob> builder)
    {
        builder.ToTable("StorageBlobs");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.StorageNodeId)
            .IsRequired();

        builder.Property(b => b.Sha256Hash)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(b => b.Sha256Hash);

        builder.Property(b => b.SizeBytes)
            .IsRequired();

        builder.Property(b => b.StorageRelativePath)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(b => b.ReferenceCount)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.HasOne(b => b.StorageNode)
            .WithMany(n => n.Blobs)
            .HasForeignKey(b => b.StorageNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
