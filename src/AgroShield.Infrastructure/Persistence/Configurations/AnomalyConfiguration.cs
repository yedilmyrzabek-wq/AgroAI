using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class AnomalyConfiguration : IEntityTypeConfiguration<Anomaly>
{
    public void Configure(EntityTypeBuilder<Anomaly> builder)
    {
        builder.ToTable("anomalies");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.EntityType).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ResolutionNotes).HasMaxLength(1000);
        builder.Property(a => a.DetectedAt).HasDefaultValueSql("now()");

        builder.Property(a => a.Reasons)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>(),
                new ValueComparer<string[]>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    v => v.ToArray()));

        builder.Property(a => a.RelatedFarmIds).HasColumnType("uuid[]");
        builder.Property(a => a.MlFeaturesJson).HasColumnType("jsonb");
        builder.Property(a => a.FrozenBatchesCount).HasDefaultValue(0);

        builder.HasIndex(a => new { a.Status, a.DetectedAt });
        builder.HasIndex(a => a.FarmId);

        builder.HasOne(a => a.Farm)
            .WithMany(f => f.Anomalies)
            .HasForeignKey(a => a.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
