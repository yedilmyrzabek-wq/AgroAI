using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class SubsidyTrancheConfiguration : IEntityTypeConfiguration<SubsidyTranche>
{
    public void Configure(EntityTypeBuilder<SubsidyTranche> builder)
    {
        builder.ToTable("subsidy_tranches");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.PercentOfTotal).HasPrecision(5, 2);
        builder.Property(t => t.AmountKzt).HasPrecision(14, 2);
        builder.Property(t => t.Status).HasMaxLength(20).IsRequired();
        builder.Property(t => t.UnlockCondition).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ReleaseEvidenceJson).HasColumnType("jsonb");
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(t => new { t.SubsidyId, t.Order }).IsUnique();
        builder.HasIndex(t => new { t.Status, t.UnlockCondition });

        builder.HasOne(t => t.Subsidy)
            .WithMany(s => s.Tranches)
            .HasForeignKey(t => t.SubsidyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
