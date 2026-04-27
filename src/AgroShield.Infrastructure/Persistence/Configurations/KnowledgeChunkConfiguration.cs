using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(k => k.SourceDoc).HasMaxLength(200).IsRequired();
        builder.Property(k => k.SourceUrl).HasMaxLength(500);
        builder.Property(k => k.EmbeddingJson).HasColumnType("jsonb");
        builder.Property(k => k.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(k => k.SourceDoc);
    }
}
