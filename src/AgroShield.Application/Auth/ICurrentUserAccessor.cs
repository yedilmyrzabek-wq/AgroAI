using AgroShield.Domain.Enums;

namespace AgroShield.Application.Auth;

public interface ICurrentUserAccessor
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string Email { get; }
    Role Role { get; }
}
