using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Concurrent;

namespace AgroShield.Infrastructure.Persistence;

public class AnomalyEscalationInterceptor(IBackgroundJobClient jobs) : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<DbContext, List<Guid>> pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, ct);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) pending.TryRemove(eventData.Context, out _);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    {
        if (eventData.Context is not null) pending.TryRemove(eventData.Context, out _);
        return base.SaveChangesFailedAsync(eventData, ct);
    }

    private void Capture(DbContext? ctx)
    {
        if (ctx is null) return;
        var ids = ctx.ChangeTracker.Entries<Anomaly>()
            .Where(e => e.State == EntityState.Added && e.Entity.RiskScore >= 85)
            .Select(e =>
            {
                if (e.Entity.Id == Guid.Empty) e.Entity.Id = Guid.NewGuid();
                return e.Entity.Id;
            })
            .ToList();
        if (ids.Count > 0) pending[ctx] = ids;
    }

    private void Flush(DbContext? ctx)
    {
        if (ctx is null) return;
        if (!pending.TryRemove(ctx, out var ids)) return;
        foreach (var id in ids)
            jobs.Enqueue<IVoiceEscalationService>(s => s.RunAsync(id, CancellationToken.None));
    }
}
