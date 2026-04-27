using AgroShield.Infrastructure.Persistence;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class WeeklyReportJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyReportJob> logger)
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var subscribedUserIds = await db.NotificationSubscriptions
            .Where(s => s.NotificationType == "weekly_report" && s.Enabled)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync();

        logger.LogInformation("WeeklyReportJob: generating reports for {Count} users", subscribedUserIds.Count);

        foreach (var userId in subscribedUserIds)
        {
            try
            {
                var farmIds = await db.Farms
                    .Where(f => f.OwnerId == userId)
                    .Select(f => f.Id)
                    .ToListAsync();

                if (farmIds.Count == 0) continue;

                // call the reports endpoint on ourselves via HTTP or directly via service
                var client = http.CreateClient("SelfInternal");
                var payload = new { user_id = userId, farm_ids = farmIds };
                await client.PostAsJsonAsync("/api/internal/reports/weekly/generate", payload, SnakeOpts);

                logger.LogInformation("WeeklyReportJob: report generated for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WeeklyReportJob failed for user {UserId}", userId);
            }
        }

        logger.LogInformation("WeeklyReportJob: completed");
    }
}
