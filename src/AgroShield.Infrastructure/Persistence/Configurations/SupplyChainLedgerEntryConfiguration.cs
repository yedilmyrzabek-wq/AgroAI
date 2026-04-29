using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SupplyChainLedgerEntryConfiguration : IEntityTypeConfiguration<SupplyChainLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplyChainLedgerEntry> builder)
    {
        builder.ToTable("supply_chain_ledger");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.BatchId).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(80).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntryHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(e => new { e.BatchId, e.CreatedAt }).HasDatabaseName("ix_supply_chain_ledger_batch_time");
        builder.HasIndex(e => e.EntryHash).HasDatabaseName("ix_supply_chain_ledger_hash");
    }
}
