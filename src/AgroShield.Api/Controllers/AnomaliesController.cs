using AgroShield.Application.DTOs.Anomalies;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/anomalies")]
[Authorize(Roles = "Inspector,Admin")]
public class AnomaliesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AnomalyFilterDto filter, CancellationToken ct)
    {
        var q = db.Anomalies.Include(a => a.Farm).AsQueryable();

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<AnomalyStatus>(filter.Status, true, out var st))
            q = q.Where(a => a.Status == st);
        if (filter.FarmId.HasValue)    q = q.Where(a => a.FarmId == filter.FarmId.Value);
        if (filter.Month.HasValue)     q = q.Where(a => a.DetectedAt.Month == filter.Month.Value);
        if (filter.Year.HasValue)      q = q.Where(a => a.DetectedAt.Year == filter.Year.Value);
        if (filter.MinRiskScore.HasValue) q = q.Where(a => a.RiskScore >= filter.MinRiskScore.Value);

        var page  = Math.Max(1, filter.Page);
        var limit = Math.Clamp(filter.Limit, 1, 200);
        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.DetectedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => ToDto(a))
            .ToListAsync(ct);

        return Ok(new { total, page, limit, items });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var anomaly = await db.Anomalies.Include(a => a.Farm)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException($"Anomaly {id} not found");

        return Ok(ToDto(anomaly));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateAnomalyStatusDto dto,
        CancellationToken ct)
    {
        var anomaly = await db.Anomalies.FindAsync([id], ct)
            ?? throw new NotFoundException($"Anomaly {id} not found");

        if (!Enum.TryParse<AnomalyStatus>(dto.NewStatus, true, out var newStatus))
            throw new Domain.Exceptions.ValidationException($"Invalid status '{dto.NewStatus}'");

        anomaly.Status = newStatus;
        if (newStatus is AnomalyStatus.Closed or AnomalyStatus.Rejected)
        {
            anomaly.ResolvedAt = DateTime.UtcNow;
            anomaly.ResolutionNotes = dto.Notes;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { id = anomaly.Id, status = anomaly.Status.ToString() });
    }

    [HttpGet("chart")]
    public async Task<IActionResult> Chart([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days).Date;
        var data = await db.Anomalies
            .Where(a => a.DetectedAt >= since)
            .GroupBy(a => a.DetectedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count(), avgRisk = g.Average(a => a.RiskScore) })
            .OrderBy(x => x.date)
            .ToListAsync(ct);

        return Ok(data);
    }

    private static AnomalyDto ToDto(Domain.Entities.Anomaly a) =>
        new(a.Id, a.EntityType.ToString(), a.EntityId, a.FarmId,
            a.Farm?.Name, a.RiskScore, a.Reasons, a.Status.ToString(),
            a.DetectedAt, a.ResolvedAt, a.ResolutionNotes);
}
