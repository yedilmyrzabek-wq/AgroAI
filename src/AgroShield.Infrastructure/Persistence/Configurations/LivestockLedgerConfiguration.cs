using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class LivestockLedgerConfiguration : IEntityTypeConfiguration<LivestockLedger>
{
    public void Configure(EntityTypeBuilder<LivestockLedger> builder)
    {
        builder.ToTable("livestock_ledger");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.LivestockType).HasMaxLength(50).IsRequired();
        builder.Property(l => l.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(l => l.EntryHash).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Source).HasMaxLength(50).IsRequired();
        builder.Property(l => l.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(l => l.EntryHash).IsUnique();
        builder.HasIndex(l => new { l.FarmId, l.CreatedAt });

        builder.HasOne(l => l.Farm)
            .WithMany()
            .HasForeignKey(l => l.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
