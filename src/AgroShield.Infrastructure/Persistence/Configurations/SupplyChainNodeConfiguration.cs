using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SupplyChainNodeConfiguration : IEntityTypeConfiguration<SupplyChainNode>
{
    public void Configure(EntityTypeBuilder<SupplyChainNode> builder)
    {
        builder.ToTable("supply_chain_nodes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.FromEntityName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ToEntityName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Product).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Quantity).HasPrecision(12, 3);
        builder.Property(s => s.Unit).HasMaxLength(30).IsRequired();
        builder.Property(s => s.TransactionHash).HasMaxLength(256).IsRequired();
        builder.Property(s => s.PreviousHash).HasMaxLength(256).IsRequired();
        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(s => s.TransactionHash).IsUnique();
        builder.HasIndex(s => new { s.IsSuspicious, s.TransactionDate });
    }
}
