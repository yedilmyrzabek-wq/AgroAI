using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class CleanupExpiredCodesJob(IServiceScopeFactory scopeFactory, ILogger<CleanupExpiredCodesJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var deleted = await db.EmailVerificationCodes
            .Where(c => c.UsedAt != null || c.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();
        logger.LogInformation("CleanupExpiredCodesJob: deleted {Count} records", deleted);
    }
}
