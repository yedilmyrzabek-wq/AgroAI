using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class NdviDropDetectionJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NdviDropDetectionJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var farms = await db.Farms
            .Where(f => f.NdviMean.HasValue && f.NdviHistoryJson != null)
            .ToListAsync();

        logger.LogInformation("NdviDropDetectionJob: checking {Count} farms", farms.Count);

        foreach (var farm in farms)
        {
            try
            {
                var history = ParseHistory(farm.NdviHistoryJson!);
                if (history.Count < 2) continue;

                var previous = history[^2].Value;
                var current = farm.NdviMean!.Value;

                if (previous <= 0) continue;

                var drop = (previous - current) / previous;
                if (drop > 0.15m)
                {
                    logger.LogWarning("NDVI drop {Drop:P0} detected on farm {FarmId}", drop, farm.Id);

                    db.Anomalies.Add(new Anomaly
                    {
                        Id = Guid.NewGuid(),
                        EntityType = AnomalyType.Ndvi,
                        EntityId = farm.Id,
                        FarmId = farm.Id,
                        RiskScore = 60 + (int)(drop * 100),
                        Reasons = [$"NDVI dropped {drop:P0} (from {previous:F3} to {current:F3})"],
                        Status = AnomalyStatus.Active,
                        DetectedAt = DateTime.UtcNow,
                    });

                    await db.SaveChangesAsync();

                    // notify subscribed users
                    var owners = await db.Users
                        .Where(u => u.Id == farm.OwnerId && u.TelegramChatId != null)
                        .Select(u => new { u.Id, u.TelegramChatId })
                        .ToListAsync();

                    foreach (var owner in owners)
                    {
                        var subExists = await db.NotificationSubscriptions
                            .AnyAsync(s => s.UserId == owner.Id && s.NotificationType == "ndvi_drop" && s.Enabled);

                        if (!subExists || owner.TelegramChatId is null) continue;

                        try
                        {
                            var tgClient = http.CreateClient("TelegramBot");
                            var msg = $"⚠️ *NDVI упал на {drop:P0}* для фермы **{farm.Name}**\n" +
                                      $"Предыдущее значение: {previous:F3}, текущее: {current:F3}";
                            await tgClient.PostAsJsonAsync("/send", new
                            {
                                telegram_chat_id = owner.TelegramChatId.Value,
                                message = msg,
                                parse_mode = "Markdown",
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to send NDVI drop notification to user {UserId}", owner.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NdviDropDetectionJob failed for farm {FarmId}", farm.Id);
            }
        }

        logger.LogInformation("NdviDropDetectionJob: completed");
    }

    private static List<(DateTime Date, decimal Value)> ParseHistory(string json)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<JsonElement[]>(json);
            if (arr is null) return [];
            return arr
                .Select(e => (
                    Date: e.TryGetProperty("date", out var d) ? DateTime.Parse(d.GetString()!) : DateTime.MinValue,
                    Value: e.TryGetProperty("value", out var v) ? v.GetDecimal() : 0m))
                .OrderBy(x => x.Date)
                .ToList();
        }
        catch { return []; }
    }
}
