using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class AnimalActivityConfiguration : IEntityTypeConfiguration<AnimalActivity>
{
    public void Configure(EntityTypeBuilder<AnimalActivity> builder)
    {
        builder.ToTable("animal_activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.DeviceId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.DetectedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => new { a.AnimalId, a.DetectedAt });

        builder.HasOne(a => a.Animal)
            .WithMany(a => a.Activities)
            .HasForeignKey(a => a.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
