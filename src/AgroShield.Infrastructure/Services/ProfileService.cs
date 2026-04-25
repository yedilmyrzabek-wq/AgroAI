using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Infrastructure.Services;

public class ProfileService(AppDbContext db) : IProfileService
{
    public async Task<Profile> GetOrCreateAsync(
        Guid userId, string email, Role role, CancellationToken ct = default)
    {
        var profile = await db.Profiles.FindAsync([userId], ct);
        if (profile is not null) return profile;

        profile = new Profile
        {
            UserId = userId,
            FullName = email.Split('@')[0],
            Role = role,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<Profile> UpdateAsync(
        Guid userId, string? fullName, string? phoneNumber, CancellationToken ct = default)
    {
        var profile = await db.Profiles.FindAsync([userId], ct)
            ?? throw new KeyNotFoundException($"Profile {userId} not found");

        if (fullName is not null) profile.FullName = fullName;
        if (phoneNumber is not null) profile.PhoneNumber = phoneNumber;
        profile.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return profile;
    }
}
