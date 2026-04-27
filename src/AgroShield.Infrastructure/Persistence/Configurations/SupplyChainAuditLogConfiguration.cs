using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SupplyChainAuditLogConfiguration : IEntityTypeConfiguration<SupplyChainAuditLog>
{
    public void Configure(EntityTypeBuilder<SupplyChainAuditLog> builder)
    {
        builder.ToTable("supply_chain_audit_log");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Reason).HasColumnType("text");
        builder.Property(a => a.MetadataJson).HasColumnType("jsonb");
        builder.Property(a => a.PerformedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => new { a.BatchId, a.PerformedAt })
            .HasDatabaseName("ix_audit_batch_time")
            .IsDescending(false, true);
        builder.HasIndex(a => new { a.Action, a.PerformedAt })
            .HasDatabaseName("ix_audit_action")
            .IsDescending(false, true);

        builder.HasOne(a => a.Batch)
            .WithMany()
            .HasForeignKey(a => a.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.PerformedByUser)
            .WithMany()
            .HasForeignKey(a => a.PerformedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
