using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Data.Configurations;

public class UploadChunkConfiguration : IEntityTypeConfiguration<UploadChunk>
{
    public void Configure(EntityTypeBuilder<UploadChunk> builder)
    {
        builder.ToTable("UploadChunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UploadSessionId)
            .IsRequired();

        builder.Property(c => c.ChunkIndex)
            .IsRequired();

        builder.Property(c => c.SizeBytes)
            .IsRequired();

        builder.Property(c => c.Sha256Hash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.IsUploaded)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.UploadedAt)
            .IsRequired();

        builder.HasIndex(c => new { c.UploadSessionId, c.ChunkIndex })
            .IsUnique();

        builder.HasOne(c => c.UploadSession)
            .WithMany(s => s.Chunks)
            .HasForeignKey(c => c.UploadSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
