using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SensorReadingConfiguration : IEntityTypeConfiguration<SensorReading>
{
    public void Configure(EntityTypeBuilder<SensorReading> builder)
    {
        builder.ToTable("sensor_readings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityAlwaysColumn();
        builder.Property(r => r.DeviceId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Temp).HasPrecision(5, 2);
        builder.Property(r => r.Humidity).HasPrecision(5, 2);
        builder.Property(r => r.RecordedAt).HasDefaultValueSql("now()");

        builder.HasIndex(r => new { r.FarmId, r.RecordedAt }).IsDescending(false, true);

        builder.HasOne(r => r.Farm)
            .WithMany()
            .HasForeignKey(r => r.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
