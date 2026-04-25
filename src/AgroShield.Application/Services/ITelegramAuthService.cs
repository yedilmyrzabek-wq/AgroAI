using AgroShield.Application.DTOs.Auth;

namespace AgroShield.Application.Services;

public interface ITelegramAuthService
{
    Task BotStartRegistrationAsync(string email);
    Task<BotUserContext> BotCompleteRegistrationAsync(BotRegisterVerifyRequest req);
    Task<BotUserContext> BotLinkExistingAsync(BotLinkExistingRequest req);
    Task<BotUserContext?> GetByChatIdAsync(long chatId);
}
