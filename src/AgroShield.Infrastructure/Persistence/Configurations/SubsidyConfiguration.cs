using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SubsidyConfiguration : IEntityTypeConfiguration<Subsidy>
{
    public void Configure(EntityTypeBuilder<Subsidy> builder)
    {
        builder.ToTable("subsidies");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.Amount).HasPrecision(14, 2);
        builder.Property(s => s.DeclaredArea).HasPrecision(10, 2);
        builder.Property(s => s.ActiveAreaFromNdvi).HasPrecision(10, 2);
        builder.Property(s => s.NdviMeanScore).HasPrecision(5, 4);
        builder.Property(s => s.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.CropType).HasMaxLength(50);
        builder.Property(s => s.WorkflowStatus).HasMaxLength(20).HasDefaultValue("approved");
        builder.Property(s => s.SubmittedAt).HasDefaultValueSql("now()");

        builder.HasIndex(s => new { s.FarmId, s.Status });
        builder.HasIndex(s => s.WorkflowStatus).HasDatabaseName("ix_subsidies_workflow_status");

        builder.HasIndex(s => new { s.Status, s.CheckedAt })
            .HasFilter("checked_at IS NULL");

        builder.HasOne(s => s.Farm)
            .WithMany(f => f.Subsidies)
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
