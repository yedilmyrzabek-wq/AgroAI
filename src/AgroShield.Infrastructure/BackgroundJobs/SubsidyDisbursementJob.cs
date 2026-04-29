using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class SubsidyDisbursementJob(
    AppDbContext db,
    ISupplyChainService supplyChain,
    IHttpClientFactory factory,
    ILogger<SubsidyDisbursementJob> logger)
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task ExecuteAsync()
    {
        var ct = CancellationToken.None;
        var subsidies = await db.Subsidies
            .Include(s => s.Tranches)
            .Include(s => s.Farm)
            .Where(s => s.WorkflowStatus == "approved" || s.WorkflowStatus == "in_progress")
            .ToListAsync(ct);

        var releasedThisRun = 0;
        var completedThisRun = 0;

        foreach (var subsidy in subsidies)
        {
            // Process tranches in order, stopping at the first not-yet-met condition
            foreach (var tranche in subsidy.Tranches.OrderBy(t => t.Order))
            {
                if (tranche.Status != "pending") continue;

                bool canRelease;
                string? evidenceSummary = null;
                try
                {
                    (canRelease, evidenceSummary) = await CheckUnlockConditionAsync(subsidy, tranche, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unlock check failed for tranche {Tranche}", tranche.Id);
                    canRelease = false;
                }

                if (!canRelease) break;

                tranche.Status = "released";
                tranche.ReleasedAt = DateTime.UtcNow;
                if (evidenceSummary is not null)
                    tranche.ReleaseEvidenceJson = evidenceSummary;
                releasedThisRun++;

                await supplyChain.AppendAsync(
                    $"subsidy-{subsidy.Id:N}",
                    "tranche_released",
                    new
                    {
                        tranche_order = tranche.Order,
                        amount_kzt = tranche.AmountKzt,
                        unlock_condition = tranche.UnlockCondition,
                        evidence = evidenceSummary,
                    },
                    actorId: null, ct);
            }

            if (subsidy.Tranches.All(t => t.Status == "released"))
            {
                subsidy.WorkflowStatus = "completed";
                subsidy.CompletedAt = DateTime.UtcNow;
                completedThisRun++;
            }
            else if (subsidy.Tranches.Any(t => t.Status == "released"))
            {
                subsidy.WorkflowStatus = "in_progress";
            }
        }

        await db.SaveChangesAsync(ct);

        if (releasedThisRun > 0 || completedThisRun > 0)
            logger.LogInformation("SubsidyDisbursementJob: released {Released} tranches, completed {Completed} subsidies", releasedThisRun, completedThisRun);
    }

    private async Task<(bool, string?)> CheckUnlockConditionAsync(Subsidy subsidy, SubsidyTranche tranche, CancellationToken ct)
    {
        return tranche.UnlockCondition switch
        {
            "registered" => (true, null),
            "sowing_confirmed" => await CheckSowingAsync(subsidy, ct),
            "fertilizer_applied" => await CheckFertilizerAsync(subsidy, ct),
            "harvest_recorded" => await CheckHarvestAsync(subsidy, ct),
            _ => (false, null),
        };
    }

    private async Task<(bool, string?)> CheckSowingAsync(Subsidy subsidy, CancellationToken ct)
    {
        var farm = subsidy.Farm;
        // Heuristic: NDVI in last 14 days has at least one reading > 0.18 → sowing happened
        try
        {
            var client = factory.CreateClient("SatelliteNdvi");
            var dateTo = DateTime.UtcNow.Date;
            var dateFrom = subsidy.SubmittedAt.Date.AddDays(-7);
            if ((dateTo - dateFrom).TotalDays > 30) dateFrom = dateTo.AddDays(-30);

            var polygon = BuildSquarePolygon(farm.Lat, farm.Lng, (double)farm.AreaHectares);
            var resp = await client.PostAsJsonAsync("/time-series", new
            {
                polygon,
                date_from = dateFrom.ToString("yyyy-MM-dd"),
                date_to = dateTo.ToString("yyyy-MM-dd"),
                interval = "weekly",
            }, SnakeOpts, ct);

            if (!resp.IsSuccessStatusCode)
                return FallbackSowingFromFarm(farm);

            var ts = await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            if (!ts.TryGetProperty("points", out var pointsEl) || pointsEl.ValueKind != JsonValueKind.Array)
                return FallbackSowingFromFarm(farm);

            double maxNdvi = 0;
            foreach (var p in pointsEl.EnumerateArray())
            {
                if (!p.TryGetProperty("ndvi", out var ndviProp) || ndviProp.ValueKind != JsonValueKind.Number) continue;
                var v = ndviProp.GetDouble();
                if (v > maxNdvi) maxNdvi = v;
            }
            var ok = maxNdvi > 0.18;
            var evidence = JsonSerializer.Serialize(new
            {
                source = "satellite-ndvi.time-series",
                max_ndvi = maxNdvi,
                threshold = 0.18,
                ok,
            }, SnakeOpts);
            return (ok, evidence);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sowing check failed for subsidy {Id}, falling back to farm.NdviMean", subsidy.Id);
            return FallbackSowingFromFarm(farm);
        }
    }

    private static (bool, string?) FallbackSowingFromFarm(Farm farm)
    {
        var ndvi = (double?)farm.NdviMean ?? 0;
        var ok = ndvi > 0.18;
        var evidence = JsonSerializer.Serialize(new
        {
            source = "farm.NdviMean",
            ndvi_mean = ndvi,
            threshold = 0.18,
            ok,
        });
        return (ok, evidence);
    }

    private async Task<(bool, string?)> CheckFertilizerAsync(Subsidy subsidy, CancellationToken ct)
    {
        var ledger = await supplyChain.GetLedgerAsync($"subsidy-{subsidy.Id:N}", ct);
        var entry = ledger
            .Where(r => r.EventType == "fertilizer_application_recorded")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();
        if (entry is null) return (false, null);

        try
        {
            var doc = JsonDocument.Parse(entry.PayloadJson);
            var ml = doc.RootElement.TryGetProperty("ml_result", out var mlEl) ? mlEl : default;
            var verdict = ml.ValueKind == JsonValueKind.Object && ml.TryGetProperty("verdict", out var vEl) ? vEl.GetString() : null;
            var ok = verdict == "applied_confirmed" || verdict == "applied_partial";
            var evidence = JsonSerializer.Serialize(new
            {
                source = "ledger.fertilizer_application_recorded",
                verdict,
                ledger_hash = entry.EntryHash,
                ok,
            }, SnakeOpts);
            return (ok, evidence);
        }
        catch
        {
            return (false, null);
        }
    }

    private async Task<(bool, string?)> CheckHarvestAsync(Subsidy subsidy, CancellationToken ct)
    {
        var ledger = await supplyChain.GetLedgerAsync($"subsidy-{subsidy.Id:N}", ct);
        var entry = ledger.FirstOrDefault(r => r.EventType == "harvest_recorded");
        if (entry is null) return (false, null);
        var evidence = JsonSerializer.Serialize(new
        {
            source = "ledger.harvest_recorded",
            ledger_hash = entry.EntryHash,
            ok = true,
        }, SnakeOpts);
        return (true, evidence);
    }

    private static double[][] BuildSquarePolygon(double lat, double lng, double areaHa)
    {
        var sideMeters = Math.Sqrt(areaHa * 10000);
        var degLat = sideMeters / 111000;
        var degLng = sideMeters / (111000 * Math.Max(0.000001, Math.Cos(lat * Math.PI / 180)));
        return new[]
        {
            new[] { lat - degLat / 2, lng - degLng / 2 },
            new[] { lat + degLat / 2, lng - degLng / 2 },
            new[] { lat + degLat / 2, lng + degLng / 2 },
            new[] { lat - degLat / 2, lng + degLng / 2 },
            new[] { lat - degLat / 2, lng - degLng / 2 },
        };
    }
}
