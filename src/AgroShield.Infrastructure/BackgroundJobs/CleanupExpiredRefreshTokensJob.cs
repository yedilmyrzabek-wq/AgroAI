using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class CleanupExpiredRefreshTokensJob(IServiceScopeFactory scopeFactory, ILogger<CleanupExpiredRefreshTokensJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var deleted = await db.RefreshTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();
        logger.LogInformation("CleanupExpiredRefreshTokensJob: deleted {Count} records", deleted);
    }
}
