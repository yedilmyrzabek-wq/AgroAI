using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgroShield.Infrastructure.Persistence.Configurations;

public class EmailVerificationCodeConfiguration : IEntityTypeConfiguration<EmailVerificationCode>
{
    public void Configure(EntityTypeBuilder<EmailVerificationCode> builder)
    {
        builder.ToTable("email_verification_codes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Email).HasMaxLength(255).IsRequired();
        builder.HasIndex(e => e.Email);
        builder.Property(e => e.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Purpose).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Attempts).HasDefaultValue(0);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
    }
}
