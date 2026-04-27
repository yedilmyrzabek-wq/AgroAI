using AgroShield.Application.Auth;
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
public class DashboardController(AppDbContext db, ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var farms = db.Farms.AsQueryable();
        if (user.Role == Role.Inspector && !string.IsNullOrWhiteSpace(user.Region))
            farms = farms.Where(f => f.Region == user.Region);

        var farmIds = await farms.Select(f => f.Id).ToListAsync(ct);

        var totalFarms = farmIds.Count;

        var activeAnomalies = await db.Anomalies
            .CountAsync(a => farmIds.Contains(a.FarmId)
                          && (a.Status == AnomalyStatus.Active || a.Status == AnomalyStatus.InProgress), ct);

        var suspiciousAmount = await db.Subsidies
            .Where(s => farmIds.Contains(s.FarmId)
                     && s.ActiveAreaFromNdvi.HasValue
                     && s.ActiveAreaFromNdvi < s.DeclaredArea * 0.7m)
            .SumAsync(s => s.Amount, ct);

        var sickPlantsToday = await db.PlantDiagnoses
            .CountAsync(d => farmIds.Contains(d.FarmId) && !d.IsHealthy && d.CreatedAt >= today, ct);

        var avgRisk = totalFarms == 0 ? 0d : await farms.AverageAsync(f => (double)f.RiskScore, ct);

        return Ok(new DashboardStatsDto(
            totalFarms,
            activeAnomalies,
            suspiciousAmount,
            sickPlantsToday,
            Math.Round(avgRisk, 1)));
    }
}
