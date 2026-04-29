using AgroShield.Api.Filters;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal")]
[InternalApiKey]
public class InternalQueryController(AppDbContext db, ILogger<InternalQueryController> logger) : ControllerBase
{
    // ── Anomalies ──────────────────────────────────────────────────────────

    [HttpGet("anomalies")]
    public async Task<IActionResult> GetAnomalies(
        [FromQuery] string? status,
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] int? min_risk_score,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        try
        {
            var q = db.Anomalies.Include(a => a.Farm).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<AnomalyStatus>(status, true, out var st))
                q = q.Where(a => a.Status == st);
            if (month.HasValue) q = q.Where(a => a.DetectedAt.Month == month.Value);
            if (year.HasValue)  q = q.Where(a => a.DetectedAt.Year == year.Value);
            if (min_risk_score.HasValue) q = q.Where(a => a.RiskScore >= min_risk_score.Value);

            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(a => a.DetectedAt).Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct);

            var subsidyIds = rows.Where(a => a.EntityType == AnomalyType.Subsidy).Select(a => a.EntityId).ToHashSet();
            var subsidyAmounts = subsidyIds.Count > 0
                ? await db.Subsidies.Where(s => subsidyIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Amount, ct)
                : new Dictionary<Guid, decimal>();

            var items = rows.Select(a => new
            {
                id = a.Id,
                farm_id = a.FarmId,
                farm_name = a.Farm?.Name,
                risk_score = a.RiskScore,
                reason = (a.Reasons?.Length ?? 0) > 0 ? a.Reasons![0] : "Без указанной причины",
                amount = a.EntityType == AnomalyType.Subsidy && subsidyAmounts.TryGetValue(a.EntityId, out var amt) ? amt : (decimal?)null,
                status = a.Status.ToString(),
                detected_at = a.DetectedAt,
            });

            return Ok(new { items, total });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAnomalies failed");
            return StatusCode(500, new { error = "internal_error", message = ex.Message, type = ex.GetType().Name });
        }
    }

    // ── Farms list ─────────────────────────────────────────────────────────

    [HttpGet("farms")]
    public async Task<IActionResult> GetFarms(
        [FromQuery] string? region,
        [FromQuery] string? crop_type,
        [FromQuery] int? min_risk,
        [FromQuery] string? sort_by,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var q = db.Farms.AsQueryable();
        if (!string.IsNullOrEmpty(region))   q = q.Where(f => f.Region == region);
        if (!string.IsNullOrEmpty(crop_type)) q = q.Where(f => f.CropType == crop_type);
        if (min_risk.HasValue) q = q.Where(f => f.RiskScore >= min_risk.Value);

        q = sort_by?.ToLower() switch
        {
            "risk" => q.OrderByDescending(f => f.RiskScore),
            "area" => q.OrderByDescending(f => f.AreaHectares),
            "name" => q.OrderBy(f => f.Name),
            _      => q.OrderByDescending(f => f.RiskScore),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Take(Math.Clamp(limit, 1, 100))
            .Select(f => new
            {
                id = f.Id,
                name = f.Name,
                region = f.Region,
                area_hectares = f.AreaHectares,
                crop_type = f.CropType,
                risk_score = f.RiskScore,
            })
            .ToListAsync(ct);

        return Ok(new { items, total });
    }

    // ── Farm detail by ID ──────────────────────────────────────────────────

    [HttpGet("farms/{id:guid}")]
    public async Task<IActionResult> GetFarmById(Guid id, CancellationToken ct) =>
        Ok(await BuildFarmDetailAsync(db.Farms.Where(f => f.Id == id), ct));

    // ── Farm detail by name ────────────────────────────────────────────────

    [HttpGet("farms/by-name/{name}")]
    public async Task<IActionResult> GetFarmByName(string name, CancellationToken ct) =>
        Ok(await BuildFarmDetailAsync(
            db.Farms.Where(f => EF.Functions.ILike(f.Name, $"%{name}%")), ct));

    // ── Subsidies stats ────────────────────────────────────────────────────

    [HttpGet("subsidies/stats")]
    public async Task<IActionResult> SubsidiesStats(CancellationToken ct)
    {
        try
        {
            // Project to in-memory shape first to avoid loading nav-property graphs that may have schema drift
            var all = await db.Subsidies
                .Select(s => new
                {
                    s.Id,
                    s.Amount,
                    s.DeclaredArea,
                    s.ActiveAreaFromNdvi,
                    Region = s.Farm != null ? s.Farm.Region : "Unknown",
                })
                .ToListAsync(ct);

            bool IsSuspicious(decimal? active, decimal declared) =>
                active.HasValue && active.Value < declared * 0.7m;

            var suspicious = all.Where(s => IsSuspicious(s.ActiveAreaFromNdvi, s.DeclaredArea)).ToList();

            var byRegion = all
                .GroupBy(s => s.Region ?? "Unknown")
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        count = g.Count(),
                        suspicious = g.Count(s => IsSuspicious(s.ActiveAreaFromNdvi, s.DeclaredArea)),
                    });

            return Ok(new
            {
                total_count = all.Count,
                total_amount = all.Sum(s => s.Amount),
                suspicious_count = suspicious.Count,
                suspicious_amount = suspicious.Sum(s => s.Amount),
                by_region = byRegion,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SubsidiesStats failed");
            return StatusCode(500, new { error = "internal_error", message = ex.Message, type = ex.GetType().Name });
        }
    }

    // ── Plant diagnoses today ──────────────────────────────────────────────

    [HttpGet("plant-diagnoses/today")]
    public async Task<IActionResult> PlantDiagnosesToday(
        [FromQuery] string? severity,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var q = db.PlantDiagnoses.Include(d => d.Farm)
            .Where(d => d.CreatedAt >= today && !d.IsHealthy);

        if (!string.IsNullOrEmpty(severity)) q = q.Where(d => d.Severity == severity);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(d => new
            {
                id = d.Id,
                farm_id = d.FarmId,
                farm_name = d.Farm.Name,
                disease_ru = d.DiseaseRu,
                severity = d.Severity,
                confidence = d.Confidence,
                detected_at = d.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(new { items, total });
    }

    // ── Dashboard stats ────────────────────────────────────────────────────

    [HttpGet("dashboard/stats")]
    public async Task<IActionResult> DashboardStats(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var totalFarms      = await db.Farms.CountAsync(ct);
        var activeAnomalies = await db.Anomalies.CountAsync(
            a => a.Status == AnomalyStatus.Active || a.Status == AnomalyStatus.InProgress, ct);
        var suspiciousAmount = await db.Subsidies
            .Where(s => s.ActiveAreaFromNdvi.HasValue && s.ActiveAreaFromNdvi < s.DeclaredArea * 0.7m)
            .SumAsync(s => s.Amount, ct);
        var sickPlantsToday = await db.PlantDiagnoses
            .CountAsync(d => !d.IsHealthy && d.CreatedAt >= today, ct);
        var avgRisk = totalFarms > 0
            ? await db.Farms.AverageAsync(f => (double)f.RiskScore, ct)
            : 0.0;

        return Ok(new
        {
            total_farms = totalFarms,
            active_anomalies = activeAnomalies,
            suspicious_amount = suspiciousAmount,
            sick_plants_today = sickPlantsToday,
            average_risk_score = Math.Round(avgRisk, 1),
        });
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private async Task<object> BuildFarmDetailAsync(IQueryable<Domain.Entities.Farm> farmQuery, CancellationToken ct)
    {
        var farm = await farmQuery.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Farm not found");

        var latest = await db.SensorReadings
            .Where(r => r.FarmId == farm.Id)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

        var activeSubsidies = await db.Subsidies.CountAsync(
            s => s.FarmId == farm.Id && (s.Status == SubsidyStatus.Pending || s.Status == SubsidyStatus.UnderReview), ct);

        var ndviMean = await db.Subsidies
            .Where(s => s.FarmId == farm.Id && s.NdviMeanScore.HasValue)
            .OrderByDescending(s => s.CheckedAt)
            .Select(s => s.NdviMeanScore)
            .FirstOrDefaultAsync(ct);

        var lastDiag = await db.PlantDiagnoses
            .Where(d => d.FarmId == farm.Id)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.DiseaseRu, d.Severity, detected_at = d.CreatedAt })
            .FirstOrDefaultAsync(ct);

        return new
        {
            id = farm.Id,
            name = farm.Name,
            region = farm.Region,
            area_hectares = farm.AreaHectares,
            crop_type = farm.CropType,
            risk_score = farm.RiskScore,
            current_temp_c = latest?.Temp,
            current_humidity_pct = latest?.Humidity,
            active_subsidies = activeSubsidies,
            ndvi_mean = ndviMean,
            last_diagnosis = lastDiag,
        };
    }
}
