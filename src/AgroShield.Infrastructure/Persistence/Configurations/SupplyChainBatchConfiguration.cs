using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SupplyChainBatchConfiguration : IEntityTypeConfiguration<SupplyChainBatch>
{
    public void Configure(EntityTypeBuilder<SupplyChainBatch> builder)
    {
        builder.ToTable("supply_chain_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.BatchCode).HasMaxLength(64).IsRequired();
        builder.Property(b => b.CropType).HasMaxLength(50).IsRequired();
        builder.Property(b => b.InitialWeightKg).HasPrecision(10, 2);
        builder.Property(b => b.CurrentWeightKg).HasPrecision(10, 2);
        builder.Property(b => b.CurrentHolderType).HasMaxLength(50).HasDefaultValue("farm");
        builder.Property(b => b.CurrentHolderId).HasMaxLength(100);
        builder.Property(b => b.Status).HasMaxLength(50).HasDefaultValue("active");
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(b => b.FreezeReason).HasColumnType("text");

        builder.HasIndex(b => b.BatchCode).IsUnique();
        builder.HasIndex(b => new { b.FarmId, b.Status });
        builder.HasIndex(b => new { b.Status, b.FarmId })
            .HasDatabaseName("ix_batches_status_farm");

        builder.HasOne(b => b.Farm)
            .WithMany(f => f.SupplyChainBatches)
            .HasForeignKey(b => b.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
