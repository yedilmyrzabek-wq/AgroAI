using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class PlantDiagnosisConfiguration : IEntityTypeConfiguration<PlantDiagnosis>
{
    public void Configure(EntityTypeBuilder<PlantDiagnosis> builder)
    {
        builder.ToTable("plant_diagnoses");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.ImageUrl).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Disease).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DiseaseRu).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Confidence).HasPrecision(5, 4);
        builder.Property(p => p.Severity).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Recommendation).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.ModelVersion).HasMaxLength(50).IsRequired();
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(p => new { p.FarmId, p.CreatedAt });

        builder.HasIndex(p => new { p.IsHealthy, p.CreatedAt })
            .HasFilter("is_healthy = false");

        builder.HasOne(p => p.Farm)
            .WithMany(f => f.PlantDiagnoses)
            .HasForeignKey(p => p.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
