using AgroShield.Application.DTOs.ML;
using AgroShield.Application.Services;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/yield")]
[Authorize]
public class YieldController(AppDbContext db, IMLProxyService ml) : ControllerBase
{
    [HttpGet("{farmId:guid}")]
    public async Task<IActionResult> Predict(Guid farmId, CancellationToken ct)
    {
        var farm = await db.Farms.FindAsync([farmId], ct)
            ?? throw new NotFoundException($"Farm {farmId} not found");

        var since = DateTime.UtcNow.AddDays(-7);
        var readings = await db.SensorReadings
            .Where(r => r.FarmId == farmId && r.RecordedAt >= since)
            .ToListAsync(ct);

        decimal avgTemp     = readings.Count > 0 ? readings.Average(r => r.Temp)     : 20m;
        decimal avgHumidity = readings.Count > 0 ? readings.Average(r => r.Humidity) : 55m;
        decimal avgLight    = readings.Count > 0 ? (decimal)readings.Average(r => r.Light) : 600m;

        var lastNdvi = await db.Subsidies
            .Where(s => s.FarmId == farmId && s.NdviMeanScore.HasValue)
            .OrderByDescending(s => s.CheckedAt)
            .Select(s => s.NdviMeanScore)
            .FirstOrDefaultAsync(ct) ?? 0.65m;

        var result = await ml.PredictYieldAsync(new YieldFeaturesDto
        {
            AvgTemp     = avgTemp,
            AvgHumidity = avgHumidity,
            AvgLight    = avgLight,
            NdviMean    = lastNdvi,
            AreaHectares = farm.AreaHectares,
            CropType    = farm.CropType,
        }, ct);

        return Ok(result);
    }
}
