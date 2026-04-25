using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class AnimalConfiguration : IEntityTypeConfiguration<Animal>
{
    public void Configure(EntityTypeBuilder<Animal> builder)
    {
        builder.ToTable("animals");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.RfidTag).HasMaxLength(50);
        builder.Property(a => a.Species).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => a.RfidTag).IsUnique().HasFilter("rfid_tag IS NOT NULL");

        builder.HasOne(a => a.Farm)
            .WithMany(f => f.Animals)
            .HasForeignKey(a => a.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
