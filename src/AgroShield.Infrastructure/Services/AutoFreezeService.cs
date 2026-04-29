using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class AutoFreezeService(
    AppDbContext db,
    IRealtimePublisher realtime,
    INotificationDispatcher notifications,
    ILogger<AutoFreezeService> logger) : IAutoFreezeService
{
    private const int RiskThreshold = 90;
    private const int RulesThreshold = 5;

    public async Task RunAsync(Guid anomalyId, CancellationToken ct = default)
    {
        var anomaly = await db.Anomalies
            .Include(a => a.Farm)
            .FirstOrDefaultAsync(a => a.Id == anomalyId, ct);
        if (anomaly is null)
        {
            logger.LogWarning("AutoFreezeService: anomaly {Id} not found", anomalyId);
            return;
        }

        if (anomaly.RiskScore < RiskThreshold) return;

        var rulesTriggered = CountRules(anomaly);
        if (rulesTriggered < RulesThreshold)
        {
            logger.LogInformation("AutoFreezeService: anomaly {Id} risk={Risk} rules={Rules} below threshold", anomalyId, anomaly.RiskScore, rulesTriggered);
            return;
        }

        var farmIds = (anomaly.RelatedFarmIds ?? [])
            .Append(anomaly.FarmId)
            .Distinct()
            .ToArray();

        var batches = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .Where(b => farmIds.Contains(b.FarmId) && b.Status == "active")
            .Take(100)
            .ToListAsync(ct);

        if (batches.Count == 0) return;

        var now = DateTime.UtcNow;
        var reason = $"Auto: risk={anomaly.RiskScore}, rules={rulesTriggered}";
        var frozenIds = new List<Guid>();

        foreach (var batch in batches)
        {
            batch.Status = "frozen";
            batch.FrozenAt = now;
            batch.FrozenBy = null; // system
            batch.FreezeReason = reason;

            db.SupplyChainAuditLogs.Add(new SupplyChainAuditLog
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Action = "auto_freeze",
                PerformedBy = Guid.Empty,
                PerformedAt = now,
                Reason = reason,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    anomaly_id = anomaly.Id,
                    risk_score = anomaly.RiskScore,
                    rules_triggered = rulesTriggered,
                    source = "AutoFreezeService",
                }),
            });
            frozenIds.Add(batch.Id);
        }

        anomaly.FrozenBatchesCount = frozenIds.Count;
        anomaly.LastFreezeAt = now;
        await db.SaveChangesAsync(ct);

        // Notifications — region-targeted SignalR + per-batch dispatcher
        var topReasons = anomaly.Reasons.Take(3).ToArray();
        try
        {
            await realtime.PushBatchFrozenAsync(anomaly.FarmId, new
            {
                type = "auto_freeze",
                farm_id = anomaly.FarmId,
                anomaly_id = anomaly.Id,
                risk_score = anomaly.RiskScore,
                rules_count = rulesTriggered,
                top_reasons = topReasons,
                frozen_batches = frozenIds.Count,
            });
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime PushBatchFrozenAsync failed"); }

        foreach (var id in frozenIds)
        {
            try { await notifications.SendBatchFrozenAsync(id); }
            catch (Exception ex) { logger.LogWarning(ex, "NotificationDispatcher.SendBatchFrozenAsync failed for {BatchId}", id); }
        }

        logger.LogWarning("AutoFreezeService: froze {Count} batches for anomaly {Id} (risk={Risk}, rules={Rules})",
            frozenIds.Count, anomaly.Id, anomaly.RiskScore, rulesTriggered);
    }

    private static int CountRules(Anomaly anomaly)
    {
        // Prefer ML features (rules_triggered list) when available; fall back to Reasons array.
        if (!string.IsNullOrEmpty(anomaly.MlFeaturesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(anomaly.MlFeaturesJson);
                if (doc.RootElement.TryGetProperty("rules_triggered", out var rules) && rules.ValueKind == JsonValueKind.Array)
                    return rules.GetArrayLength();
            }
            catch { }
        }
        return anomaly.Reasons?.Length ?? 0;
    }
}
