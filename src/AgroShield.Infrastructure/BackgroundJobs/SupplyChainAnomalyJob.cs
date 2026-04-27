using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class SupplyChainAnomalyJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SupplyChainAnomalyJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var batches = await db.SupplyChainBatches
            .Where(b => b.Status == "active" && !b.AnomalyDetected)
            .ToListAsync();

        logger.LogInformation("SupplyChainAnomalyJob: checking {Count} active batches", batches.Count);

        foreach (var batch in batches)
        {
            try
            {
                bool anomalyDetected = false;
                string? reason = null;

                try
                {
                    var client = http.CreateClient("SupplyChainTracker");
                    var response = await client.GetAsync($"/anomaly-check/{batch.Id}");
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                        anomalyDetected = result.TryGetProperty("anomaly_detected", out var ad) && ad.GetBoolean();
                        reason = result.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    }
                }
                catch
                {
                    // fallback: weight loss check
                    if (batch.InitialWeightKg > 0)
                    {
                        var loss = (batch.InitialWeightKg - batch.CurrentWeightKg) / batch.InitialWeightKg;
                        anomalyDetected = loss > 0.2m;
                        reason = anomalyDetected ? $"Weight loss {loss:P0} exceeds 20% threshold" : null;
                    }
                }

                if (anomalyDetected)
                {
                    batch.AnomalyDetected = true;
                    db.Anomalies.Add(new Anomaly
                    {
                        Id = Guid.NewGuid(),
                        EntityType = AnomalyType.SupplyChain,
                        EntityId = batch.Id,
                        FarmId = batch.FarmId,
                        RiskScore = 65,
                        Reasons = [reason ?? "Supply chain anomaly detected by scheduled check"],
                        Status = AnomalyStatus.Active,
                        DetectedAt = DateTime.UtcNow,
                    });

                    await db.SaveChangesAsync();
                    logger.LogWarning("Anomaly detected in batch {BatchCode}", batch.BatchCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SupplyChainAnomalyJob failed for batch {BatchId}", batch.Id);
            }
        }

        logger.LogInformation("SupplyChainAnomalyJob: completed");
    }
}
