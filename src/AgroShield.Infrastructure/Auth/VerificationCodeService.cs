using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace AgroShield.Infrastructure.Auth;

public class VerificationCodeService(AppDbContext db, IConfiguration config) : IVerificationCodeService
{
    public async Task<string> GenerateAsync(string email, string purpose, CancellationToken ct = default)
    {
        var old = await db.EmailVerificationCodes
            .Where(c => c.Email == email && c.Purpose == purpose && c.UsedAt == null)
            .ToListAsync(ct);
        db.EmailVerificationCodes.RemoveRange(old);

        var bytes = RandomNumberGenerator.GetBytes(4);
        var number = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
        var code = number.ToString("D6");

        var ttl = int.Parse(config["VerificationCodes:TtlMinutes"] ?? "10");

        db.EmailVerificationCodes.Add(new EmailVerificationCode
        {
            Email = email,
            CodeHash = HashCode(code),
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ttl),
        });

        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<bool> VerifyAsync(string email, string code, string purpose, CancellationToken ct = default)
    {
        var maxAttempts = int.Parse(config["VerificationCodes:MaxAttempts"] ?? "5");
        var hash = HashCode(code);

        var entity = await db.EmailVerificationCodes
            .Where(c => c.Email == email && c.Purpose == purpose
                        && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (entity is null) return false;

        entity.Attempts++;

        if (entity.Attempts > maxAttempts)
        {
            entity.UsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (entity.CodeHash != hash)
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        entity.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLower();
}
