using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class StorageNodeConfiguration : IEntityTypeConfiguration<StorageNode>
{
    public void Configure(EntityTypeBuilder<StorageNode> builder)
    {
        builder.ToTable("StorageNodes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(n => n.RegistrationKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(n => n.RegistrationKey)
            .IsUnique();

        builder.Property(n => n.NodeSecretHash)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.BaseUrl)
            .HasMaxLength(256);

        builder.Property(n => n.IsBehindNat)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.TotalSpaceBytes)
            .IsRequired();

        builder.Property(n => n.AvailableSpaceBytes)
            .IsRequired();

        builder.Property(n => n.MaxCapacityBytes)
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(n => n.CpuLoad);

        builder.Property(n => n.DiskStatus)
            .HasMaxLength(64);

        builder.Property(n => n.LatencyMs);

        builder.Property(n => n.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(n => n.LastHeartbeatAt)
            .IsRequired();

        builder.HasIndex(n => n.LastHeartbeatAt);

        builder.Property(n => n.CreatedAt)
            .IsRequired();
    }
}
