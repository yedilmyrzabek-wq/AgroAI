using AgroShield.Api.Filters;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/sensors")]
[InternalApiKey]
public class InternalSensorsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{farmId:guid}/history")]
    public async Task<IActionResult> GetHistory(
        Guid farmId,
        [FromQuery] string period = "24h",
        [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var (since, defaultLimit) = ParsePeriod(period);
        var take = Math.Clamp(limit ?? defaultLimit, 1, 5000);

        var rows = await db.SensorReadings
            .Where(r => r.FarmId == farmId && r.RecordedAt >= since)
            .OrderBy(r => r.RecordedAt)
            .Take(take)
            .Select(r => new
            {
                recorded_at = r.RecordedAt,
                temp = r.Temp,
                humidity = r.Humidity,
                light = r.Light,
                fire = r.Fire,
                water_level = r.WaterLevel,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            farm_id = farmId,
            farm_name = farm.Name,
            period,
            since,
            count = rows.Count,
            points = rows,
        });
    }

    private static (DateTime Since, int DefaultLimit) ParsePeriod(string period)
    {
        var now = DateTime.UtcNow;
        return (period?.ToLowerInvariant() ?? "24h") switch
        {
            "1h"  => (now.AddHours(-1),    200),
            "6h"  => (now.AddHours(-6),    400),
            "12h" => (now.AddHours(-12),   600),
            "24h" => (now.AddHours(-24),   1000),
            "3d"  => (now.AddDays(-3),     1500),
            "7d"  => (now.AddDays(-7),     2000),
            "30d" => (now.AddDays(-30),    3000),
            _     => (now.AddHours(-24),   1000),
        };
    }
}
