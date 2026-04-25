using AgroShield.Application.DTOs.ML;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class EvaluateAnomaliesJob(
    IServiceScopeFactory scopeFactory,
    ILogger<EvaluateAnomaliesJob> logger)
{
    public async Task ExecuteAsync()
    {
        List<Guid> subsidyIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            subsidyIds = await db.Subsidies
                .Where(s => (s.Status == SubsidyStatus.Pending || s.Status == SubsidyStatus.UnderReview)
                            && s.CheckedAt == null)
                .Take(20)
                .Select(s => s.Id)
                .ToListAsync();
        }

        logger.LogInformation("EvaluateAnomaliesJob: processing {Count} subsidies", subsidyIds.Count);

        await Parallel.ForEachAsync(subsidyIds,
            new ParallelOptions { MaxDegreeOfParallelism = 3 },
            async (id, ct) =>
        {
            using var scope = scopeFactory.CreateScope();
            var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ml       = scope.ServiceProvider.GetRequiredService<IMLProxyService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IRealtimePublisher>();

            try
            {
                var subsidy = await db.Subsidies.Include(s => s.Farm)
                    .FirstAsync(s => s.Id == id, ct);
                var farm = subsidy.Farm;
                var now  = DateTime.UtcNow;

                var polygon = ParsePolygon(farm.PolygonGeoJson, farm.Lat, farm.Lng);
                var ndvi = await ml.GetNdviAsync(new NdviRequestDto
                {
                    Polygon  = polygon,
                    DateFrom = now.AddDays(-30).ToString("yyyy-MM-dd"),
                    DateTo   = now.ToString("yyyy-MM-dd"),
                }, ct);

                subsidy.ActiveAreaFromNdvi = ndvi.ActiveAreaHectares;
                subsidy.NdviMeanScore      = ndvi.MeanNdvi;
                subsidy.CheckedAt          = now;

                var readingCount = await db.SensorReadings.CountAsync(
                    r => r.FarmId == farm.Id && r.RecordedAt >= now.AddHours(-24), ct);
                var sensorHours = Math.Min(readingCount / 6m, 24m);

                var result = await ml.CheckAnomalyAsync(new SubsidyCheckDto
                {
                    DeclaredArea            = subsidy.DeclaredArea,
                    Amount                  = subsidy.Amount,
                    ActiveAreaNdvi          = ndvi.ActiveAreaHectares,
                    SensorActiveHoursPerDay = sensorHours,
                    CropType                = farm.CropType,
                }, ct);

                if (result.IsSuspicious)
                {
                    var anomaly = new Anomaly
                    {
                        Id         = Guid.NewGuid(),
                        EntityType = AnomalyType.Subsidy,
                        EntityId   = subsidy.Id,
                        FarmId     = farm.Id,
                        RiskScore  = result.RiskScore,
                        Reasons    = [.. result.Reasons],
                        Status     = AnomalyStatus.Active,
                        DetectedAt = now,
                    };
                    db.Anomalies.Add(anomaly);

                    db.Alerts.Add(new Alert
                    {
                        Id        = Guid.NewGuid(),
                        Type      = AlertType.Anomaly,
                        Title     = $"Аномалия субсидии: {farm.Name}",
                        Message   = $"NDVI: {ndvi.ActiveAreaHectares:F1} га / задекларировано: {subsidy.DeclaredArea} га. {string.Join("; ", result.Reasons)}",
                        FarmId    = farm.Id,
                        IsRead    = false,
                        CreatedAt = now,
                    });
                }

                await db.SaveChangesAsync(ct);

                if (result.IsSuspicious)
                {
                    try
                    {
                        await publisher.PushFireAlertAsync(farm.Id, new
                        {
                            type       = "subsidy_anomaly",
                            farmId     = farm.Id,
                            farmName   = farm.Name,
                            riskScore  = result.RiskScore,
                            reasons    = result.Reasons,
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "SignalR push failed for farm {FarmId}", farm.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EvaluateAnomaliesJob failed for subsidy {SubsidyId}", id);
            }
        });
    }

    private static List<double[]> ParsePolygon(string geoJson, double lat, double lng)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(geoJson);
            if (doc.TryGetProperty("coordinates", out var coords))
            {
                var points = new List<double[]>();
                foreach (var pt in coords[0].EnumerateArray())
                {
                    var arr = pt.EnumerateArray().ToArray();
                    points.Add([arr[1].GetDouble(), arr[0].GetDouble()]);
                }
                return points;
            }
        }
        catch { }
        const double d = 0.01;
        return [[lat-d,lng-d],[lat-d,lng+d],[lat+d,lng+d],[lat+d,lng-d],[lat-d,lng-d]];
    }
}
