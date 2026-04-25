using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;

namespace AgroShield.Application.Services;

public interface IProfileService
{
    Task<Profile> GetOrCreateAsync(Guid userId, string email, Role role, CancellationToken ct = default);
    Task<Profile> UpdateAsync(Guid userId, string? fullName, string? phoneNumber, CancellationToken ct = default);
}
