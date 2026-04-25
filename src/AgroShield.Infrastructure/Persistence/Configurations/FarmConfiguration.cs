using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> builder)
    {
        builder.ToTable("farms");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Region).HasMaxLength(100).IsRequired();
        builder.Property(f => f.District).HasMaxLength(100).IsRequired();
        builder.Property(f => f.AreaHectares).HasPrecision(10, 2);
        builder.Property(f => f.CropType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.DeviceId).HasMaxLength(100);
        builder.Property(f => f.PolygonGeoJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'");
        builder.Property(f => f.RiskScore).HasDefaultValue(0);
        builder.Property(f => f.NdviMean).HasPrecision(5, 4);
        builder.Property(f => f.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(f => f.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(f => f.DeviceId)
            .HasFilter("device_id IS NOT NULL");

        builder.HasIndex(f => f.RiskScore);
        builder.HasIndex(f => f.OwnerId);
    }
}
