using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class FertilizerRecommendationConfiguration : IEntityTypeConfiguration<FertilizerRecommendation>
{
    public void Configure(EntityTypeBuilder<FertilizerRecommendation> builder)
    {
        builder.ToTable("fertilizer_recommendations");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.NKgPerHa).HasPrecision(6, 2);
        builder.Property(f => f.PKgPerHa).HasPrecision(6, 2);
        builder.Property(f => f.KKgPerHa).HasPrecision(6, 2);
        builder.Property(f => f.TotalKg).HasPrecision(10, 2);
        builder.Property(f => f.EstimatedCostKzt).HasPrecision(12, 2);
        builder.Property(f => f.ExpectedYieldIncreasePct).HasPrecision(5, 2);
        builder.Property(f => f.ApplicationWindows).HasColumnType("jsonb");
        builder.Property(f => f.ModelVersion).HasMaxLength(20);
        builder.Property(f => f.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(f => new { f.FarmId, f.CreatedAt });

        builder.HasOne(f => f.Farm)
            .WithMany(fm => fm.FertilizerRecommendations)
            .HasForeignKey(f => f.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
