using AgroShield.Application.Services;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class VoiceEscalationService(
    AppDbContext db,
    IHttpClientFactory factory,
    IMemoryCache cache,
    ILogger<VoiceEscalationService> logger) : IVoiceEscalationService
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);

    public async Task RunAsync(Guid anomalyId, CancellationToken ct = default)
    {
        var anomaly = await db.Anomalies
            .Include(a => a.Farm)
            .FirstOrDefaultAsync(a => a.Id == anomalyId, ct);
        if (anomaly is null) return;

        var inspector = await db.Users
            .Where(u => u.Role == Role.Inspector && u.AssignedRegion == anomaly.Farm.Region)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (inspector?.TelegramChatId is null)
        {
            logger.LogInformation("VoiceEscalation: no inspector with Telegram for region {Region}", anomaly.Farm.Region);
            return;
        }

        var dedupKey = $"voice-escalation:{inspector.Id}:critical";
        if (cache.TryGetValue(dedupKey, out _))
        {
            logger.LogInformation("VoiceEscalation: cooldown active for inspector {Id}", inspector.Id);
            return;
        }

        var text = $"Внимание. Критическая аномалия по ферме {anomaly.Farm.Name}. Риск {anomaly.RiskScore}. Проверьте срочно.";
        string? audioBase64 = null;

        try
        {
            var ai = factory.CreateClient("AiAssistant");
            var resp = await ai.PostAsJsonAsync("/tts", new { text, voice = "ru-RU" }, SnakeOpts, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
                audioBase64 = json.TryGetProperty("audio_base64", out var ab) ? ab.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS unavailable, falling back to text message");
        }

        try
        {
            var bot = factory.CreateClient("TelegramBot");
            if (audioBase64 is not null)
            {
                await bot.PostAsJsonAsync("/send-voice", new
                {
                    telegram_chat_id = inspector.TelegramChatId.Value,
                    audio_base64 = audioBase64,
                    caption = text,
                }, SnakeOpts, ct);
            }
            else
            {
                await bot.PostAsJsonAsync("/send", new
                {
                    telegram_chat_id = inspector.TelegramChatId.Value,
                    message = $"⚠️ {text}",
                    parse_mode = "Markdown",
                }, SnakeOpts, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voice escalation send failed");
            return;
        }

        cache.Set(dedupKey, true, Cooldown);
    }
}
