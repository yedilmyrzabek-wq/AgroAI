using AgroShield.Domain.Entities;

namespace AgroShield.Infrastructure.Auth;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    (string plain, string hash) GenerateRefreshToken();
}
