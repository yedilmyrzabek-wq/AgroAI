using AgroShield.Application.DTOs.Auth;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Auth;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Infrastructure.Services;

public class TelegramAuthService(
    AppDbContext db,
    IAuthService auth,
    IPasswordHasher hasher,
    IVerificationCodeService codes) : ITelegramAuthService
{
    public Task BotStartRegistrationAsync(string email) =>
        auth.StartRegistrationAsync(email);

    public async Task<BotUserContext> BotCompleteRegistrationAsync(BotRegisterVerifyRequest req)
    {
        var normalized = req.Email.ToLowerInvariant().Trim();
        if (!await codes.VerifyAsync(normalized, req.Code, "Registration"))
            throw new ValidationException("Неверный код");

        var chatTaken = await db.Users.AnyAsync(u => u.TelegramChatId == req.TelegramChatId && u.Email != normalized);
        if (chatTaken)
            throw new ConflictException("Этот Telegram аккаунт уже привязан к другому пользователю");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                PasswordHash = hasher.Hash(req.Password),
                Role = Role.Farmer,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
        }
        else
        {
            user.PasswordHash = hasher.Hash(req.Password);
        }

        user.IsEmailVerified = true;
        user.TelegramChatId = req.TelegramChatId;
        user.TelegramUsername = req.TelegramUsername;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return await ToBotContextAsync(user);
    }

    public async Task<BotUserContext> BotLinkExistingAsync(BotLinkExistingRequest req)
    {
        var normalized = req.Email.ToLowerInvariant().Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);

        if (user is null || user.PasswordHash is null || !hasher.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials");

        var chatTaken = await db.Users.AnyAsync(u => u.TelegramChatId == req.TelegramChatId && u.Id != user.Id);
        if (chatTaken)
            throw new ConflictException("Этот Telegram аккаунт уже привязан к другому пользователю");

        user.TelegramChatId = req.TelegramChatId;
        user.TelegramUsername = req.TelegramUsername;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return await ToBotContextAsync(user);
    }

    public async Task<BotUserContext?> GetByChatIdAsync(long chatId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
        if (user is null) return null;
        return await ToBotContextAsync(user);
    }

    private async Task<BotUserContext> ToBotContextAsync(User user)
    {
        var farmId = await db.Farms
            .Where(f => f.OwnerId == user.Id)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync();

        return new BotUserContext(user.Id, user.Email, user.FullName, user.Role.ToString().ToLower(), farmId);
    }
}
