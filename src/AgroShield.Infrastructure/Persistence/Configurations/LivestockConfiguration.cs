using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class LivestockConfiguration : IEntityTypeConfiguration<Livestock>
{
    public void Configure(EntityTypeBuilder<Livestock> builder)
    {
        builder.ToTable("livestock");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.LivestockType).HasMaxLength(50).IsRequired();
        builder.Property(l => l.LastImageUrl).HasMaxLength(500);
        builder.Property(l => l.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(l => l.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(l => l.FarmId);

        builder.HasOne(l => l.Farm)
            .WithMany(f => f.Livestock)
            .HasForeignKey(l => l.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
