using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.Resource)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.ResourceId)
            .HasMaxLength(64);

        builder.Property(a => a.UserName)
            .HasMaxLength(64);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45)
            .IsRequired();

        builder.Property(a => a.Timestamp)
            .IsRequired();

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => a.Timestamp);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
