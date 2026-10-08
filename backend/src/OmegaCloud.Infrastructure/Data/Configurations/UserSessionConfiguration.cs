using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RefreshTokenHash)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(s => s.RefreshTokenHash);

        builder.Property(s => s.DeviceName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(s => s.IpAddress)
            .HasMaxLength(45)
            .IsRequired();

        builder.Property(s => s.UserAgent)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.ExpiresAt)
            .IsRequired();

        builder.Property(s => s.LastActiveAt)
            .IsRequired();

        builder.Property(s => s.RevokedAt);

        builder.Property(s => s.ReplacedByTokenHash)
            .HasMaxLength(256);

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
