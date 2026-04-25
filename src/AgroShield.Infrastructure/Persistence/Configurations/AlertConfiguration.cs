using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.IsRead).HasDefaultValue(false);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => new { a.UserId, a.IsRead, a.CreatedAt });

        // SignalR push and fire-alert queries filter by farm_id
        builder.HasIndex(a => new { a.FarmId, a.CreatedAt })
            .HasFilter("farm_id IS NOT NULL");

        builder.HasOne<Profile>()
            .WithMany(p => p.Alerts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
