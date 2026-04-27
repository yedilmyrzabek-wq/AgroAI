using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SupplyChainTransitionConfiguration : IEntityTypeConfiguration<SupplyChainTransition>
{
    public void Configure(EntityTypeBuilder<SupplyChainTransition> builder)
    {
        builder.ToTable("supply_chain_transitions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.FromNodeType).HasMaxLength(50).IsRequired();
        builder.Property(t => t.FromNodeId).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ToNodeType).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ToNodeId).HasMaxLength(100).IsRequired();
        builder.Property(t => t.WeightKg).HasPrecision(10, 2);

        builder.HasIndex(t => new { t.BatchId, t.TransferredAt });

        builder.HasOne(t => t.Batch)
            .WithMany(b => b.Transitions)
            .HasForeignKey(t => t.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
