using AgroShield.Api.Filters;
using AgroShield.Application.Services;
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
    IStorageService storage,
    ILogger<InternalLivestockController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly HashSet<string> CvEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cv_increment", "cv_full_recount"
    };

    private static readonly HashSet<string> ManualEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "lost", "sold", "born", "bought", "manual_adjustment"
    };

    [HttpPost("count-from-image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CountFromImage(
        [FromForm] CountFromImageRequest request,
        CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == request.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var isFullRecount = string.Equals(request.Mode, "full_recount", StringComparison.OrdinalIgnoreCase);
        var eventType = isFullRecount ? "cv_full_recount" : "cv_increment";

        // Upload original image to Storage for audit trail (TZ §10)
        string? photoUrl = null;
        byte[] fileBytes;
        await using (var ms = new MemoryStream())
        {
            await using var src = request.File.OpenReadStream();
            await src.CopyToAsync(ms, ct);
            fileBytes = ms.ToArray();
        }

        try
        {
            var key = $"livestock/{request.FarmId}/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}{Path.GetExtension(request.File.FileName)}";
            await using var uploadStream = new MemoryStream(fileBytes);
            photoUrl = await storage.UploadAsync(uploadStream, key, request.File.ContentType ?? "image/jpeg");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Storage upload failed, proceeding without photo_url");
        }

        // Call livestock-monitor
        JsonElement mlResult;
        try
        {
            var client = factory.CreateClient("LivestockMonitor");
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(fileBytes), "file", request.File.FileName);
            content.Add(new StringContent(request.FarmId.ToString()), "farm_id");
            content.Add(new StringContent(request.LivestockType), "expected_class");

            var response = await client.PostAsync("/count-livestock", content, ct);
            response.EnsureSuccessStatusCode();
            mlResult = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LivestockMonitor unavailable, using mock");
            mlResult = JsonSerializer.Deserialize<JsonElement>(
                """{"total":0,"by_class":{},"boxes":[],"is_mock":true}""");
        }

        var mlTotal = mlResult.TryGetProperty("total", out var dc) ? dc.GetInt32() : 0;
        var boxesRaw = mlResult.TryGetProperty("boxes", out var b) ? b.GetRawText() : "[]";

        var livestock = await GetOrCreateLivestockAsync(request.FarmId, request.LivestockType, request.DeclaredCount ?? 0, ct);

        var headCountBefore = livestock.LastDetectedCount ?? livestock.DeclaredCount;
        var headCountAfter = isFullRecount ? mlTotal : headCountBefore + mlTotal;
        var delta = headCountAfter - headCountBefore;

        var declared = request.DeclaredCount ?? livestock.DeclaredCount;
        var anomaly = isFullRecount
            && declared > 0
            && Math.Abs(declared - headCountAfter) / (double)declared > 0.2;

        livestock.LastDetectedCount = headCountAfter;
        livestock.LastDetectedAt = DateTime.UtcNow;
        livestock.LastImageUrl = photoUrl ?? livestock.LastImageUrl;
        livestock.AnomalyDetected = anomaly;
        livestock.UpdatedAt = DateTime.UtcNow;

        var payload = new
        {
            event_type = eventType,
            ml_total = mlTotal,
            head_count_before = headCountBefore,
            head_count_after = headCountAfter,
            delta,
            declared_count = declared,
            photo_url = photoUrl,
            anomaly_detected = anomaly,
            mode = request.Mode ?? "increment",
        };
        var payloadJson = JsonSerializer.Serialize(payload, SnakeOpts);

        var ledgerEntry = await AppendLedgerAsync(
            request.FarmId, request.LivestockType, headCountAfter, eventType, payloadJson, ct);

        if (anomaly)
        {
            db.Anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                EntityType = Domain.Enums.AnomalyType.Sensor,
                EntityId = livestock.Id,
                FarmId = request.FarmId,
                RiskScore = 70,
                Reasons = [$"Livestock count anomaly: declared={declared}, detected={headCountAfter}"],
                Status = Domain.Enums.AnomalyStatus.Active,
                DetectedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        // Mirror to external SupplyChainTracker (best-effort, non-blocking) — TZ §11.2
        await MirrorToSupplyChainAsync(request.FarmId, request.LivestockType, ledgerEntry, payload, ct);

        return Ok(new
        {
            mode = isFullRecount ? "full_recount" : "increment",
            event_type = eventType,
            ml_total = mlTotal,
            head_count_before = headCountBefore,
            delta,
            head_count_after = headCountAfter,
            declared_count = declared,
            anomaly_detected = anomaly,
            photo_url = photoUrl,
            boxes = JsonSerializer.Deserialize<JsonElement>(boxesRaw),
        });
    }

    [HttpPost("count-from-url")]
    public async Task<IActionResult> CountFromUrl([FromBody] CountFromUrlRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return BadRequest(new { error = "image_url is required" });
        if (request.FarmId == Guid.Empty)
            return BadRequest(new { error = "farm_id is required" });

        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == request.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var livestockType = string.IsNullOrWhiteSpace(request.ExpectedClass) ? "sheep" : request.ExpectedClass;
        var isFullRecount = string.Equals(request.Mode, "full_recount", StringComparison.OrdinalIgnoreCase);
        var eventType = isFullRecount ? "cv_full_recount" : "cv_increment";

        JsonElement mlResult;
        try
        {
            var client = factory.CreateClient("LivestockMonitor");
            var resp = await client.PostAsJsonAsync("/count-livestock-by-url", new
            {
                image_url = request.ImageUrl,
                farm_id = request.FarmId.ToString(),
                expected_class = livestockType,
            }, SnakeOpts, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                logger.LogWarning("LivestockMonitor count-livestock-by-url returned {Status}: {Body}", resp.StatusCode, err);
                return StatusCode(502, new { error = "ml_error", status = (int)resp.StatusCode, body = err });
            }
            mlResult = await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LivestockMonitor count-livestock-by-url unavailable");
            mlResult = JsonSerializer.Deserialize<JsonElement>(
                """{"total":0,"by_class":{},"boxes":[],"is_mock":true}""");
        }

        var mlTotal = mlResult.TryGetProperty("total", out var dc) ? dc.GetInt32() : 0;
        var boxesRaw = mlResult.TryGetProperty("boxes", out var b) ? b.GetRawText() : "[]";

        var livestock = await GetOrCreateLivestockAsync(request.FarmId, livestockType, request.DeclaredCount ?? 0, ct);
        var headCountBefore = livestock.LastDetectedCount ?? livestock.DeclaredCount;
        var headCountAfter = isFullRecount ? mlTotal : headCountBefore + mlTotal;
        var delta = headCountAfter - headCountBefore;

        var declared = request.DeclaredCount ?? livestock.DeclaredCount;
        var anomaly = isFullRecount && declared > 0
            && Math.Abs(declared - headCountAfter) / (double)declared > 0.2;

        livestock.LastDetectedCount = headCountAfter;
        livestock.LastDetectedAt = DateTime.UtcNow;
        livestock.LastImageUrl = request.ImageUrl;
        livestock.AnomalyDetected = anomaly;
        livestock.UpdatedAt = DateTime.UtcNow;

        var payload = new
        {
            event_type = eventType,
            ml_total = mlTotal,
            head_count_before = headCountBefore,
            head_count_after = headCountAfter,
            delta,
            declared_count = declared,
            photo_url = request.ImageUrl,
            anomaly_detected = anomaly,
            mode = request.Mode ?? "increment",
            source = "url",
        };
        var payloadJson = JsonSerializer.Serialize(payload, SnakeOpts);

        var ledgerEntry = await AppendLedgerAsync(
            request.FarmId, livestockType, headCountAfter, eventType, payloadJson, ct);

        if (anomaly)
        {
            db.Anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                EntityType = Domain.Enums.AnomalyType.Sensor,
                EntityId = livestock.Id,
                FarmId = request.FarmId,
                RiskScore = 70,
                Reasons = [$"Livestock count anomaly: declared={declared}, detected={headCountAfter}"],
                Status = Domain.Enums.AnomalyStatus.Active,
                DetectedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        await MirrorToSupplyChainAsync(request.FarmId, livestockType, ledgerEntry, payload, ct);

        return Ok(new
        {
            mode = isFullRecount ? "full_recount" : "increment",
            event_type = eventType,
            ml_total = mlTotal,
            head_count_before = headCountBefore,
            delta,
            head_count_after = headCountAfter,
            declared_count = declared,
            anomaly_detected = anomaly,
            photo_url = request.ImageUrl,
            boxes = JsonSerializer.Deserialize<JsonElement>(boxesRaw),
        });
    }

    [HttpPost("{farmId:guid}/events")]
    public async Task<IActionResult> AppendEvent(Guid farmId, [FromBody] LivestockEventRequest request, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        if (!ManualEventTypes.Contains(request.EventType))
            return BadRequest(new { error = "invalid_event_type", allowed = ManualEventTypes });

        if (request.CountDelta == 0)
            return BadRequest(new { error = "invalid_delta", message = "count_delta must be non-zero" });

        // Sign convention: lost/sold → negative; born/bought → positive; manual_adjustment → as provided
        var signedDelta = request.EventType switch
        {
            "lost" or "sold" => -Math.Abs(request.CountDelta),
            "born" or "bought" => Math.Abs(request.CountDelta),
            _ => request.CountDelta,
        };

        var livestock = await GetOrCreateLivestockAsync(farmId, request.LivestockType, 0, ct);

        var headCountBefore = livestock.LastDetectedCount ?? livestock.DeclaredCount;
        var headCountAfter = headCountBefore + signedDelta;

        if (headCountAfter < 0)
            return BadRequest(new { error = "negative_count", message = $"Resulting count would be {headCountAfter}" });

        livestock.LastDetectedCount = headCountAfter;
        livestock.UpdatedAt = DateTime.UtcNow;

        var payload = new
        {
            event_type = request.EventType,
            head_count_before = headCountBefore,
            head_count_after = headCountAfter,
            delta = signedDelta,
            notes = request.Notes,
            actor_id = ResolveActorId(),
        };
        var payloadJson = JsonSerializer.Serialize(payload, SnakeOpts);

        var ledgerEntry = await AppendLedgerAsync(
            farmId, request.LivestockType, headCountAfter, request.EventType, payloadJson, ct);

        await db.SaveChangesAsync(ct);
        await MirrorToSupplyChainAsync(farmId, request.LivestockType, ledgerEntry, payload, ct);

        return Ok(new
        {
            event_type = request.EventType,
            head_count_before = headCountBefore,
            delta = signedDelta,
            head_count_after = headCountAfter,
            entry_hash = ledgerEntry.EntryHash,
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
            l.LastImageUrl,
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
            event_type = e.EventType,
            payload = string.IsNullOrEmpty(e.PayloadJson) ? null : (JsonElement?)JsonSerializer.Deserialize<JsonElement>(e.PayloadJson),
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

        var livestock = await GetOrCreateLivestockAsync(farmId, request.LivestockType, request.Count, ct);
        livestock.DeclaredCount = request.Count;
        livestock.UpdatedAt = DateTime.UtcNow;

        var payload = new { event_type = "manual", declared_count = request.Count };
        var payloadJson = JsonSerializer.Serialize(payload, SnakeOpts);

        var ledgerEntry = await AppendLedgerAsync(
            farmId, request.LivestockType, request.Count, "manual", payloadJson, ct);

        await db.SaveChangesAsync(ct);
        return Ok(new { success = true, entry_hash = ledgerEntry.EntryHash });
    }

    private async Task<Livestock> GetOrCreateLivestockAsync(Guid farmId, string type, int initialDeclared, CancellationToken ct)
    {
        var livestock = await db.Livestock
            .FirstOrDefaultAsync(l => l.FarmId == farmId && l.LivestockType == type, ct);

        if (livestock is null)
        {
            livestock = new Livestock
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                LivestockType = type,
                DeclaredCount = initialDeclared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Livestock.Add(livestock);
        }
        return livestock;
    }

    private async Task<LivestockLedger> AppendLedgerAsync(
        Guid farmId, string type, int count, string eventType, string payloadJson, CancellationToken ct)
    {
        var prevHash = await GetLatestHashAsync(farmId, type, ct);
        var now = DateTime.UtcNow;
        var entryHash = ComputeHash(prevHash, farmId, type, count, now);

        var entry = new LivestockLedger
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            LivestockType = type,
            Count = count,
            PrevHash = prevHash,
            EntryHash = entryHash,
            Source = CvEventTypes.Contains(eventType) ? eventType : "manual",
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedAt = now,
            CreatedByUserId = ResolveActorId(),
        };
        db.LivestockLedger.Add(entry);
        return entry;
    }

    private async Task MirrorToSupplyChainAsync(Guid farmId, string type, LivestockLedger entry, object payload, CancellationToken ct)
    {
        try
        {
            // batch_id convention for livestock chain: deterministic per (farm, type)
            var batchId = DeriveLivestockBatchId(farmId, type);
            var client = factory.CreateClient("SupplyChainTracker");
            var body = new
            {
                batch_id = batchId,
                event_type = entry.EventType ?? entry.Source,
                actor_id = entry.CreatedByUserId,
                timestamp = entry.CreatedAt,
                prev_hash = entry.PrevHash,
                hash = entry.EntryHash,
                payload,
            };
            var resp = await client.PostAsJsonAsync("/append-record", body, SnakeOpts, ct);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("SupplyChainTracker /append-record returned {Status} for livestock event {Hash}", resp.StatusCode, entry.EntryHash);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SupplyChainTracker mirror failed for livestock event {Hash} (non-blocking)", entry.EntryHash);
        }
    }

    private static Guid DeriveLivestockBatchId(Guid farmId, string type)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"livestock|{farmId:N}|{type.ToLowerInvariant()}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private Guid? ResolveActorId()
    {
        if (Request.Headers.TryGetValue("X-User-Id", out var raw) && Guid.TryParse(raw, out var g))
            return g;
        return null;
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
    public string Mode { get; set; } = "increment";
}

public class CountFromUrlRequest
{
    public string ImageUrl { get; set; } = null!;
    public Guid FarmId { get; set; }
    public string? ExpectedClass { get; set; }
    public int? DeclaredCount { get; set; }
    public string? Mode { get; set; }
}

public class DeclareRequest
{
    public string LivestockType { get; set; } = null!;
    public int Count { get; set; }
}

public class LivestockEventRequest
{
    public string LivestockType { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public int CountDelta { get; set; }
    public string? Notes { get; set; }
}
