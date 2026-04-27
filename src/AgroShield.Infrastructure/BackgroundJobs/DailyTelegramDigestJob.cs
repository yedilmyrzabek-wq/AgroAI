using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class DailyTelegramDigestJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyTelegramDigestJob> logger)
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var subscribers = await (
            from u in db.Users
            join s in db.NotificationSubscriptions on u.Id equals s.UserId
            where u.TelegramChatId != null
                  && s.NotificationType == "daily_digest"
                  && s.Enabled
            select new { u.Id, u.Email, u.FullName, ChatId = u.TelegramChatId!.Value, u.AssignedRegion }
        ).ToListAsync();

        logger.LogInformation("DailyTelegramDigestJob: {Count} subscribers", subscribers.Count);

        foreach (var u in subscribers)
        {
            try
            {
                var farms = await db.Farms
                    .Where(f => f.OwnerId == u.Id)
                    .Select(f => new { f.Id, f.Name, f.NdviMean, f.RiskScore, f.NdviUpdatedAt })
                    .ToListAsync();

                var farmIds = farms.Select(f => f.Id).ToList();
                var diagnoses = await db.PlantDiagnoses
                    .Where(d => farmIds.Contains(d.FarmId))
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(5)
                    .Select(d => new { d.FarmId, d.DiseaseRu, d.Severity, d.CreatedAt })
                    .ToListAsync();

                string markdown;
                try
                {
                    var ai = http.CreateClient("AiAssistant");
                    var resp = await ai.PostAsJsonAsync("/generate-daily-digest", new
                    {
                        user_id = u.Id,
                        full_name = u.FullName,
                        farms,
                        diagnoses,
                        date = DateOnly.FromDateTime(DateTime.UtcNow),
                    }, SnakeOpts);

                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts);
                        markdown = json.TryGetProperty("markdown", out var md) ? md.GetString() ?? FallbackMarkdown(u.FullName, farms.Count) : FallbackMarkdown(u.FullName, farms.Count);
                    }
                    else
                    {
                        markdown = FallbackMarkdown(u.FullName, farms.Count);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AiAssistant unavailable for daily digest, using fallback");
                    markdown = FallbackMarkdown(u.FullName, farms.Count);
                }

                try
                {
                    var bot = http.CreateClient("TelegramBot");
                    await bot.PostAsJsonAsync("/send", new
                    {
                        telegram_chat_id = u.ChatId,
                        message = markdown,
                        parse_mode = "Markdown",
                    }, SnakeOpts);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "TelegramBot send failed for user {UserId}", u.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DailyTelegramDigestJob failed for user {UserId}", u.Id);
            }
        }
    }

    private static string FallbackMarkdown(string? name, int farmsCount) =>
        $"*Доброе утро, {name ?? "фермер"}!* \nСегодня у вас {farmsCount} ферм(ы) под наблюдением. Подробности в приложении AgroShield.";
}
