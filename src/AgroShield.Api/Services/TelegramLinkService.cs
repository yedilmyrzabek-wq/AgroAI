using AgroShield.Application.DTOs.Telegram;
using AgroShield.Application.Services;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AgroShield.Api.Services;

public class TelegramLinkService(IMemoryCache cache, AppDbContext db) : ITelegramLinkService
{
    private static string CacheKey(string code) => $"tg-link-{code}";

    public Task<string> GenerateCodeAsync(Guid userId)
    {
        var code = Random.Shared.Next(100_000, 999_999).ToString();
        cache.Set(CacheKey(code), userId, TimeSpan.FromMinutes(10));
        return Task.FromResult(code);
    }

    public async Task<bool> LinkAsync(string code, long chatId)
    {
        if (!cache.TryGetValue(CacheKey(code), out Guid userId))
            return false;

        var user = await db.Users.FindAsync([userId]);
        if (user is null) return false;

        user.TelegramChatId = chatId;
        await db.SaveChangesAsync();
        cache.Remove(CacheKey(code));
        return true;
    }

    public async Task<FarmStatusDto?> GetFarmStatusByChatIdAsync(long chatId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
        if (user is null) return null;

        var farm = await db.Farms.FirstOrDefaultAsync(f => f.OwnerId == user.Id);
        if (farm is null) return new FarmStatusDto(true, null, null, null, null, null, null);

        var latest = await db.SensorReadings
            .Where(r => r.FarmId == farm.Id)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync();

        double? minutesAgo = latest is null ? null
            : (DateTime.UtcNow - latest.RecordedAt).TotalMinutes;

        return new FarmStatusDto(
            Linked: true,
            FarmName: farm.Name,
            TemperatureC: latest?.Temp,
            HumidityPct: latest?.Humidity,
            LightLux: latest?.Light,
            WaterLevelPct: latest is null ? null : Math.Round(latest.WaterLevel / 10m, 1),
            MinutesAgo: minutesAgo is null ? null : Math.Round(minutesAgo.Value, 1));
    }
}
