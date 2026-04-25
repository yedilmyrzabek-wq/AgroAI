namespace AgroShield.Infrastructure.Auth;

public interface IVerificationCodeService
{
    Task<string> GenerateAsync(string email, string purpose, CancellationToken ct = default);
    Task<bool> VerifyAsync(string email, string code, string purpose, CancellationToken ct = default);
}
