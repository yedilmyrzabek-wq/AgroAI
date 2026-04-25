using AgroShield.Application.DTOs.Dashboard;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Inspector,Admin")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var totalFarms = await db.Farms.CountAsync(ct);

        var activeAnomalies = await db.Anomalies
            .CountAsync(a => a.Status == AnomalyStatus.Active || a.Status == AnomalyStatus.InProgress, ct);

        var suspiciousAmount = await db.Subsidies
            .Where(s => s.ActiveAreaFromNdvi.HasValue && s.ActiveAreaFromNdvi < s.DeclaredArea * 0.7m)
            .SumAsync(s => s.Amount, ct);

        var sickPlantsToday = await db.PlantDiagnoses
            .CountAsync(d => !d.IsHealthy && d.CreatedAt >= today, ct);

        var avgRisk = await db.Farms.AverageAsync(f => (double)f.RiskScore, ct);

        return Ok(new DashboardStatsDto(
            totalFarms,
            activeAnomalies,
            suspiciousAmount,
            sickPlantsToday,
            Math.Round(avgRisk, 1)));
    }
}
