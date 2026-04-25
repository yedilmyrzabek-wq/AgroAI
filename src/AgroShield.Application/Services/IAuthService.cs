using AgroShield.Application.DTOs.Auth;

namespace AgroShield.Application.Services;

public interface IAuthService
{
    Task StartRegistrationAsync(string email);
    Task<TokenResponse> CompleteRegistrationAsync(VerifyRegistrationRequest req);
    Task<TokenResponse> LoginAsync(LoginRequest req);
    Task<TokenResponse> RefreshAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
    Task StartPasswordResetAsync(string email);
    Task ConfirmPasswordResetAsync(ConfirmPasswordResetRequest req);
    Task<UserDto> GetMeAsync(Guid userId);
}
