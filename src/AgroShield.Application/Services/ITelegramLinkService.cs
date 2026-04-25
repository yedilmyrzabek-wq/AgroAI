using AgroShield.Application.DTOs.Telegram;

namespace AgroShield.Application.Services;

public interface ITelegramLinkService
{
    Task<string> GenerateCodeAsync(Guid userId);
    Task<bool> LinkAsync(string code, long chatId);
    Task<FarmStatusDto?> GetFarmStatusByChatIdAsync(long chatId);
}
