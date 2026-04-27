using AgroShield.Application.DTOs.Auth;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Auth;
using AgroShield.Infrastructure.ExternalServices;
using AgroShield.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace AgroShield.Infrastructure.Services;

public class AuthService(
    AppDbContext db,
    IJwtTokenService jwt,
    IPasswordHasher hasher,
    IVerificationCodeService codes,
    IConfiguration config) : IAuthService
{
    public async Task StartRegistrationAsync(string email)
    {
        var normalized = email.ToLowerInvariant().Trim();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
        if (existing?.IsEmailVerified == true)
            throw new ConflictException("Email уже зарегистрирован");

        var code = await codes.GenerateAsync(normalized, "Registration");
        var (subject, html) = EmailTemplates.VerificationCodeEmail(code, "Registration");
        BackgroundJob.Enqueue<IEmailSender>(s => s.SendAsync(normalized, subject, html, CancellationToken.None));
    }

    public async Task<TokenResponse> CompleteRegistrationAsync(VerifyRegistrationRequest req)
    {
        var normalized = req.Email.ToLowerInvariant().Trim();
        if (!await codes.VerifyAsync(normalized, req.Code, "Registration"))
            throw new ValidationException("Неверный код");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), Email = normalized, CreatedAt = DateTime.UtcNow, PasswordHash = "" };
            db.Users.Add(user);
        }

        user.PasswordHash = hasher.Hash(req.Password);
        user.FullName = req.FullName;
        user.PhoneNumber = req.PhoneNumber;
        user.IsEmailVerified = true;
        user.Role = Role.Farmer;
        user.UpdatedAt = DateTime.UtcNow;

        var (plain, entity) = MakeRefreshToken(user.Id);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync();

        return await BuildResponseAsync(user, plain);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest req)
    {
        var normalized = req.Email.ToLowerInvariant().Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);

        if (user is null || !user.IsActive || !user.IsEmailVerified
            || user.PasswordHash is null || !hasher.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials");

        var (plain, entity) = MakeRefreshToken(user.Id);
        db.RefreshTokens.Add(entity);
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return await BuildResponseAsync(user, plain);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            ?? throw new UnauthorizedException("Invalid refresh token");

        token.RevokedAt = DateTime.UtcNow;

        var (plain, entity) = MakeRefreshToken(token.UserId);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync();

        return await BuildResponseAsync(token.User, plain);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var hash = HashToken(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
        if (token is null) return;
        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task StartPasswordResetAsync(string email)
    {
        var normalized = email.ToLowerInvariant().Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
        if (user is null) return;

        var code = await codes.GenerateAsync(normalized, "PasswordReset");
        var (subject, html) = EmailTemplates.VerificationCodeEmail(code, "PasswordReset");
        BackgroundJob.Enqueue<IEmailSender>(s => s.SendAsync(normalized, subject, html, CancellationToken.None));
    }

    public async Task ConfirmPasswordResetAsync(ConfirmPasswordResetRequest req)
    {
        var normalized = req.Email.ToLowerInvariant().Trim();
        if (!await codes.VerifyAsync(normalized, req.Code, "PasswordReset"))
            throw new ValidationException("Неверный код");

        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == normalized)
            ?? throw new NotFoundException("Пользователь не найден");

        user.PasswordHash = hasher.Hash(req.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        foreach (var t in user.RefreshTokens.Where(t => t.RevokedAt == null))
            t.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task<UserDto> GetMeAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new NotFoundException("Пользователь не найден");
        return ToUserDto(user, await GetFarmIdAsync(userId));
    }

    private (string plain, RefreshToken entity) MakeRefreshToken(Guid userId)
    {
        var (plain, hash) = jwt.GenerateRefreshToken();
        var days = int.Parse(config["Jwt:RefreshTokenDays"] ?? "30");
        return (plain, new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(days),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task<TokenResponse> BuildResponseAsync(User user, string plainRefresh)
    {
        var expiresIn = int.Parse(config["Jwt:AccessTokenMinutes"] ?? "60") * 60;
        return new TokenResponse(
            jwt.GenerateAccessToken(user),
            plainRefresh,
            expiresIn,
            ToUserDto(user, await GetFarmIdAsync(user.Id)));
    }

    private static UserDto ToUserDto(User user, Guid? farmId) =>
        new(user.Id, user.Email, user.FullName, user.Role.ToString().ToLower(), user.TelegramChatId.HasValue, farmId, user.AssignedRegion);

    private async Task<Guid?> GetFarmIdAsync(Guid userId) =>
        await db.Farms.Where(f => f.OwnerId == userId).Select(f => (Guid?)f.Id).FirstOrDefaultAsync();

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLower();
}
