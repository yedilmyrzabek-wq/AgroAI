using AgroShield.Application.Auth;
using AgroShield.Application.DTOs.ML;
using AgroShield.Application.DTOs.Subsidies;
using AgroShield.Application.Services;
using AgroShield.Api.Hubs;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/subsidies")]
[Authorize]
public class SubsidiesController(
    AppDbContext db,
    IMLProxyService ml,
    IHubContext<AlertsHub> alertsHub,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var q = db.Subsidies.Include(s => s.Farm).AsQueryable();

        if (user.Role == Role.Farmer)
            q = q.Where(s => s.Farm.OwnerId == user.UserId);
        else if (user.Role == Role.Inspector && !string.IsNullOrWhiteSpace(user.Region))
            q = q.Where(s => s.Farm.Region == user.Region);

        if (farmId.HasValue) q = q.Where(s => s.FarmId == farmId.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<SubsidyStatus>(status, true, out var st))
            q = q.Where(s => s.Status == st);

        var items = await q
            .OrderByDescending(s => s.SubmittedAt)
            .Select(s => ToDto(s))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var subsidy = await db.Subsidies.Include(s => s.Farm)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException($"Subsidy {id} not found");

        return Ok(ToDto(subsidy));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubsidyDto dto, CancellationToken ct)
    {
        var farmExists = await db.Farms.AnyAsync(f => f.Id == dto.FarmId, ct);
        if (!farmExists) throw new NotFoundException($"Farm {dto.FarmId} not found");

        var subsidy = new Subsidy
        {
            Id = Guid.NewGuid(),
            FarmId = dto.FarmId,
            Amount = dto.Amount,
            DeclaredArea = dto.DeclaredArea,
            Purpose = dto.Purpose,
            Status = SubsidyStatus.Pending,
            SubmittedAt = DateTime.UtcNow,
        };
        db.Subsidies.Add(subsidy);
        await db.SaveChangesAsync(ct);

        var created = await db.Subsidies.Include(s => s.Farm)
            .FirstAsync(s => s.Id == subsidy.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = subsidy.Id }, ToDto(created));
    }

    [HttpPost("{id:guid}/check-ndvi")]
    [Authorize(Roles = "Inspector,Admin")]
    public async Task<IActionResult> CheckNdvi(Guid id, CancellationToken ct)
    {
        var subsidy = await db.Subsidies.Include(s => s.Farm)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException($"Subsidy {id} not found");

        var farm = subsidy.Farm;

        // Parse polygon from GeoJSON [lng,lat] → convert to [lat,lng] for NDVI service
        var polygon = ParsePolygon(farm.PolygonGeoJson, farm.Lat, farm.Lng);
        var now = DateTime.UtcNow;
        var ndviResult = await ml.GetNdviAsync(new NdviRequestDto
        {
            Polygon = polygon,
            DateFrom = now.AddDays(-30).ToString("yyyy-MM-dd"),
            DateTo = now.ToString("yyyy-MM-dd"),
        }, ct);

        subsidy.ActiveAreaFromNdvi = ndviResult.ActiveAreaHectares;
        subsidy.NdviMeanScore = ndviResult.MeanNdvi;
        subsidy.CheckedAt = DateTime.UtcNow;

        // Sensor active hours: rough estimate from readings in last 24h
        var readingCount = await db.SensorReadings
            .CountAsync(r => r.FarmId == farm.Id && r.RecordedAt >= now.AddHours(-24), ct);
        var sensorHours = Math.Min(readingCount / 6m, 24m);

        var anomalyResult = await ml.CheckAnomalyAsync(new SubsidyCheckDto
        {
            DeclaredArea = subsidy.DeclaredArea,
            Amount = subsidy.Amount,
            ActiveAreaNdvi = ndviResult.ActiveAreaHectares,
            SensorActiveHoursPerDay = sensorHours,
            CropType = farm.CropType,
        }, ct);

        if (anomalyResult.IsSuspicious)
        {
            var anomaly = new Anomaly
            {
                Id = Guid.NewGuid(),
                EntityType = AnomalyType.Subsidy,
                EntityId = subsidy.Id,
                FarmId = farm.Id,
                RiskScore = anomalyResult.RiskScore,
                Reasons = [.. anomalyResult.Reasons],
                Status = AnomalyStatus.Active,
                DetectedAt = DateTime.UtcNow,
            };
            db.Anomalies.Add(anomaly);

            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                Type = AlertType.Anomaly,
                Title = $"Подозрительная субсидия: {farm.Name}",
                Message = $"NDVI показал активную площадь {ndviResult.ActiveAreaHectares:F1} га при задекларированной {subsidy.DeclaredArea} га. Причины: {string.Join("; ", anomalyResult.Reasons)}",
                FarmId = farm.Id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);

            await alertsHub.Clients.Group(HubGroups.Inspectors)
                .SendAsync("SubsidyAlert", new
                {
                    alertId = alert.Id,
                    farmId = farm.Id,
                    farmName = farm.Name,
                    riskScore = anomalyResult.RiskScore,
                    reasons = anomalyResult.Reasons,
                });
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            subsidyId = subsidy.Id,
            ndvi = ndviResult,
            anomalyCheck = anomalyResult,
            subsidy = ToDto(await db.Subsidies.Include(s => s.Farm).FirstAsync(s => s.Id == id, ct)),
        });
    }

    private static List<double[]> ParsePolygon(string geoJson, double centerLat, double centerLng)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(geoJson);
            if (doc.TryGetProperty("coordinates", out var coords))
            {
                var ring = coords[0];
                var points = new List<double[]>();
                foreach (var pt in ring.EnumerateArray())
                {
                    var arr = pt.EnumerateArray().ToArray();
                    // GeoJSON is [lng, lat], NDVI service expects [lat, lng]
                    points.Add([arr[1].GetDouble(), arr[0].GetDouble()]);
                }
                return points;
            }
        }
        catch { }

        // Fallback: bounding box around center
        const double d = 0.01;
        return
        [
            [centerLat - d, centerLng - d],
            [centerLat - d, centerLng + d],
            [centerLat + d, centerLng + d],
            [centerLat + d, centerLng - d],
            [centerLat - d, centerLng - d],
        ];
    }

    private static SubsidyDto ToDto(Subsidy s) =>
        new(s.Id, s.FarmId, s.Farm.Name, s.Amount, s.DeclaredArea,
            s.ActiveAreaFromNdvi, s.NdviMeanScore, s.Purpose,
            s.Status.ToString(), s.SubmittedAt, s.CheckedAt);
}
