using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/livestock")]
[InternalApiKey]
public class InternalLivestockController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalLivestockController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpPost("count-from-image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CountFromImage(
        [FromForm] CountFromImageRequest request,
        CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == request.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        // call livestock-monitor
        JsonElement mlResult;
        try
        {
            var client = factory.CreateClient("LivestockMonitor");
            using var content = new MultipartFormDataContent();
            await using var stream = request.File.OpenReadStream();
            content.Add(new StreamContent(stream), "file", request.File.FileName);
            content.Add(new StringContent(request.FarmId.ToString()), "farm_id");
            content.Add(new StringContent(request.LivestockType), "livestock_type");
            if (request.DeclaredCount.HasValue)
                content.Add(new StringContent(request.DeclaredCount.Value.ToString()), "declared_count");

            var response = await client.PostAsync("/count-livestock", content, ct);
            response.EnsureSuccessStatusCode();
            mlResult = (await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LivestockMonitor unavailable, using mock");
            mlResult = JsonSerializer.Deserialize<JsonElement>(
                """{"detected_count":0,"bboxes":[],"image_with_boxes_b64":"","_mock":true}""");
        }

        var detectedCount = mlResult.TryGetProperty("detected_count", out var dc) ? dc.GetInt32() : 0;
        var imageB64 = mlResult.TryGetProperty("image_with_boxes_b64", out var img) ? img.GetString() : "";

        // get or create livestock record
        var livestock = await db.Livestock
            .FirstOrDefaultAsync(l => l.FarmId == request.FarmId && l.LivestockType == request.LivestockType, ct);

        if (livestock is null)
        {
            livestock = new Livestock
            {
                Id = Guid.NewGuid(),
                FarmId = request.FarmId,
                LivestockType = request.LivestockType,
                DeclaredCount = request.DeclaredCount ?? detectedCount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Livestock.Add(livestock);
        }

        var declared = request.DeclaredCount ?? livestock.DeclaredCount;
        var anomaly = declared > 0 && Math.Abs(declared - detectedCount) / (double)declared > 0.2;

        livestock.LastDetectedCount = detectedCount;
        livestock.LastDetectedAt = DateTime.UtcNow;
        livestock.AnomalyDetected = anomaly;
        livestock.UpdatedAt = DateTime.UtcNow;

        // append ledger entry
        var prevHash = await GetLatestHashAsync(request.FarmId, request.LivestockType, ct);
        var entryHash = ComputeHash(prevHash, request.FarmId, request.LivestockType, detectedCount, DateTime.UtcNow);
        db.LivestockLedger.Add(new LivestockLedger
        {
            Id = Guid.NewGuid(),
            FarmId = request.FarmId,
            LivestockType = request.LivestockType,
            Count = detectedCount,
            PrevHash = prevHash,
            EntryHash = entryHash,
            Source = "cv",
            CreatedAt = DateTime.UtcNow,
        });

        if (anomaly)
        {
            db.Anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                EntityType = Domain.Enums.AnomalyType.Sensor,
                EntityId = livestock.Id,
                FarmId = request.FarmId,
                RiskScore = 70,
                Reasons = [$"Livestock count anomaly: declared={declared}, detected={detectedCount}"],
                Status = Domain.Enums.AnomalyStatus.Active,
                DetectedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            detected_count = detectedCount,
            declared_count = declared,
            anomaly_detected = anomaly,
            image_with_boxes_b64 = imageB64,
        });
    }

    [HttpGet("{farmId:guid}")]
    public async Task<IActionResult> GetByFarm(Guid farmId, CancellationToken ct)
    {
        var rows = await db.Livestock
            .Where(l => l.FarmId == farmId)
            .ToListAsync(ct);

        var ledgerSize = await db.LivestockLedger.CountAsync(l => l.FarmId == farmId, ct);

        return Ok(rows.Select(l => new
        {
            l.Id,
            l.LivestockType,
            l.DeclaredCount,
            l.LastDetectedCount,
            l.LastDetectedAt,
            l.AnomalyDetected,
            ledger_size = ledgerSize,
        }));
    }

    [HttpGet("{farmId:guid}/ledger")]
    public async Task<IActionResult> GetLedger(Guid farmId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var entries = await db.LivestockLedger
            .Where(l => l.FarmId == farmId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(entries.Select(e => new
        {
            e.Id,
            e.LivestockType,
            e.Count,
            e.Source,
            e.EntryHash,
            e.PrevHash,
            e.CreatedAt,
            e.CreatedByUserId,
        }));
    }

    [HttpGet("{farmId:guid}/verify-integrity")]
    public async Task<IActionResult> VerifyIntegrity(Guid farmId, CancellationToken ct)
    {
        var entries = await db.LivestockLedger
            .Where(l => l.FarmId == farmId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var expectedHash = ComputeHash(e.PrevHash, farmId, e.LivestockType, e.Count, e.CreatedAt);
            if (expectedHash != e.EntryHash)
                return Ok(new { valid = false, broken_at = i });
        }

        return Ok(new { valid = true, broken_at = (int?)null });
    }

    [HttpPost("{farmId:guid}/declare")]
    public async Task<IActionResult> Declare(Guid farmId, [FromBody] DeclareRequest request, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var livestock = await db.Livestock
            .FirstOrDefaultAsync(l => l.FarmId == farmId && l.LivestockType == request.LivestockType, ct);

        if (livestock is null)
        {
            livestock = new Livestock
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                LivestockType = request.LivestockType,
                DeclaredCount = request.Count,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Livestock.Add(livestock);
        }
        else
        {
            livestock.DeclaredCount = request.Count;
            livestock.UpdatedAt = DateTime.UtcNow;
        }

        var prevHash = await GetLatestHashAsync(farmId, request.LivestockType, ct);
        var entryHash = ComputeHash(prevHash, farmId, request.LivestockType, request.Count, DateTime.UtcNow);

        db.LivestockLedger.Add(new LivestockLedger
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            LivestockType = request.LivestockType,
            Count = request.Count,
            PrevHash = prevHash,
            EntryHash = entryHash,
            Source = "manual",
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { success = true, entry_hash = entryHash });
    }

    private async Task<string> GetLatestHashAsync(Guid farmId, string type, CancellationToken ct)
    {
        var latest = await db.LivestockLedger
            .Where(l => l.FarmId == farmId && l.LivestockType == type)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.EntryHash)
            .FirstOrDefaultAsync(ct);
        return latest ?? "0000000000000000000000000000000000000000000000000000000000000000";
    }

    private static string ComputeHash(string prevHash, Guid farmId, string type, int count, DateTime at)
    {
        var input = $"{prevHash}|{farmId}|{type}|{count}|{at:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public class CountFromImageRequest
{
    public IFormFile File { get; set; } = null!;
    public Guid FarmId { get; set; }
    public string LivestockType { get; set; } = null!;
    public int? DeclaredCount { get; set; }
}

public class DeclareRequest
{
    public string LivestockType { get; set; } = null!;
    public int Count { get; set; }
}
