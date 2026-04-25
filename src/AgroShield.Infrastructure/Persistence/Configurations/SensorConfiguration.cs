using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SensorConfiguration : IEntityTypeConfiguration<Sensor>
{
    public void Configure(EntityTypeBuilder<Sensor> builder)
    {
        builder.ToTable("sensors");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasOne(s => s.Farm)
            .WithMany(f => f.Sensors)
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
