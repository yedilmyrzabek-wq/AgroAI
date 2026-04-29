using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/supply-chain")]
[InternalApiKey]
public class InternalSupplyChainController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalSupplyChainController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateBatchRequest request, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == request.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var batchCode = GenerateBatchCode(request.FarmId, request.CropType);
        string? qrPng = null;

        try
        {
            var client = factory.CreateClient("SupplyChainTracker");
            var payload = new { farm_id = request.FarmId, crop_type = request.CropType, weight_kg = request.WeightKg, harvest_date = request.HarvestDate, batch_code = batchCode };
            var response = await client.PostAsJsonAsync("/create-batch", payload, SnakeOpts, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
                qrPng = result.TryGetProperty("qr_png_b64", out var qr) ? qr.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SupplyChainTracker unavailable, proceeding without QR");
        }

        var batch = new SupplyChainBatch
        {
            Id = Guid.NewGuid(),
            BatchCode = batchCode,
            FarmId = request.FarmId,
            CropType = request.CropType,
            InitialWeightKg = request.WeightKg,
            CurrentWeightKg = request.WeightKg,
            HarvestDate = request.HarvestDate,
            CurrentHolderType = "farm",
            CurrentHolderId = request.FarmId.ToString(),
            Status = "active",
            CreatedAt = DateTime.UtcNow,
        };

        db.SupplyChainBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        return Ok(new { batch_id = batch.Id, batch_code = batchCode, qr_png_b64 = qrPng, _mock = qrPng is null });
    }

    [HttpPost("batch/{id:guid}/move")]
    public async Task<IActionResult> MoveBatch(Guid id, [FromBody] MoveBatchRequest request, CancellationToken ct)
    {
        var batch = await db.SupplyChainBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return NotFound(new { error = "not_found", message = "Batch not found" });

        var actorId = ResolveActorId();

        if (batch.Status == "frozen")
        {
            if (actorId != Guid.Empty)
            {
                db.SupplyChainAuditLogs.Add(new SupplyChainAuditLog
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    Action = "move_blocked",
                    PerformedBy = actorId,
                    PerformedAt = DateTime.UtcNow,
                    Reason = "Frozen batch cannot be moved",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        blocked_reason = "frozen",
                        attempted_to_node_type = request.ToNodeType,
                        attempted_to_node_id = request.ToNodeId,
                        attempted_weight = request.WeightKg,
                    }),
                });
                await db.SaveChangesAsync(ct);
            }
            return StatusCode(409, new { error = "frozen_batch", message = "Партия заморожена и не может быть перемещена", details = (object?)null });
        }

        var transition = new SupplyChainTransition
        {
            Id = Guid.NewGuid(),
            BatchId = id,
            FromNodeType = batch.CurrentHolderType,
            FromNodeId = batch.CurrentHolderId ?? batch.FarmId.ToString(),
            ToNodeType = request.ToNodeType,
            ToNodeId = request.ToNodeId,
            WeightKg = request.WeightKg,
            TransferredAt = request.TransferredAt ?? DateTime.UtcNow,
            Notes = request.Notes,
        };

        batch.CurrentHolderType = request.ToNodeType;
        batch.CurrentHolderId = request.ToNodeId;
        batch.CurrentWeightKg = request.WeightKg;

        db.SupplyChainTransitions.Add(transition);
        if (actorId != Guid.Empty)
        {
            db.SupplyChainAuditLogs.Add(new SupplyChainAuditLog
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Action = "move",
                PerformedBy = actorId,
                PerformedAt = DateTime.UtcNow,
                Reason = request.Notes,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    from_node_type = transition.FromNodeType,
                    from_node_id = transition.FromNodeId,
                    to_node_type = transition.ToNodeType,
                    to_node_id = transition.ToNodeId,
                    weight_kg = transition.WeightKg,
                }),
            });
        }
        await db.SaveChangesAsync(ct);

        try
        {
            var client = factory.CreateClient("SupplyChainTracker");
            await client.PostAsJsonAsync($"/batch/{id}/move", request, SnakeOpts, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SupplyChainTracker move notification failed");
        }

        return Ok(new { transition_id = transition.Id, success = true });
    }

    private Guid ResolveActorId()
    {
        if (Request.Headers.TryGetValue("X-User-Id", out var raw) && Guid.TryParse(raw, out var g))
            return g;
        // System actor when no header — use deterministic fallback (zero guid)
        return Guid.Empty;
    }

    [HttpGet("batch/{id:guid}/trace")]
    public async Task<IActionResult> TraceBatch(Guid id, CancellationToken ct)
    {
        var batch = await db.SupplyChainBatches
            .Include(b => b.Transitions.OrderBy(t => t.TransferredAt))
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (batch is null)
            return NotFound(new { error = "not_found", message = "Batch not found" });

        return Ok(new
        {
            batch_id = batch.Id,
            batch_code = batch.BatchCode,
            farm_id = batch.FarmId,
            crop_type = batch.CropType,
            initial_weight_kg = batch.InitialWeightKg,
            current_weight_kg = batch.CurrentWeightKg,
            harvest_date = batch.HarvestDate,
            status = batch.Status,
            anomaly_detected = batch.AnomalyDetected,
            current_holder = new { type = batch.CurrentHolderType, id = batch.CurrentHolderId },
            timeline = batch.Transitions.Select(t => new
            {
                t.Id,
                from = new { type = t.FromNodeType, id = t.FromNodeId },
                to = new { type = t.ToNodeType, id = t.ToNodeId },
                weight_kg = t.WeightKg,
                transferred_at = t.TransferredAt,
                t.Notes,
            }),
        });
    }

    [HttpGet("batch/{id:guid}/anomaly-check")]
    public async Task<IActionResult> AnomalyCheck(Guid id, CancellationToken ct)
    {
        var batch = await db.SupplyChainBatches
            .Include(b => b.Transitions)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (batch is null)
            return NotFound(new { error = "not_found", message = "Batch not found" });

        bool anomalyDetected = false;
        string? reason = null;

        try
        {
            var client = factory.CreateClient("SupplyChainTracker");
            var response = await client.GetAsync($"/anomaly-check/{id}", ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
                anomalyDetected = result.TryGetProperty("anomaly_detected", out var ad) && ad.GetBoolean();
                reason = result.TryGetProperty("reason", out var r) ? r.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SupplyChainTracker anomaly-check unavailable");
            // fallback: detect weight loss > 20%
            if (batch.InitialWeightKg > 0)
            {
                var loss = (batch.InitialWeightKg - batch.CurrentWeightKg) / batch.InitialWeightKg;
                anomalyDetected = loss > 0.2m;
                reason = anomalyDetected ? $"Weight loss {loss:P0} exceeds threshold" : null;
            }
        }

        if (anomalyDetected)
        {
            batch.AnomalyDetected = true;
            db.Anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                EntityType = Domain.Enums.AnomalyType.SupplyChain,
                EntityId = id,
                FarmId = batch.FarmId,
                RiskScore = 65,
                Reasons = [reason ?? "Supply chain anomaly detected"],
                Status = Domain.Enums.AnomalyStatus.Active,
                DetectedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { anomaly_detected = anomalyDetected, reason });
    }

    [HttpGet("farm/{farmId:guid}/batches")]
    public async Task<IActionResult> GetFarmBatches(Guid farmId, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var query = db.SupplyChainBatches.Where(b => b.FarmId == farmId);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(b => b.Status == status);

        var batches = await query.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);

        return Ok(batches.Select(b => new
        {
            b.Id,
            b.BatchCode,
            b.CropType,
            b.InitialWeightKg,
            b.CurrentWeightKg,
            b.Status,
            b.AnomalyDetected,
            b.HarvestDate,
            current_holder = new { type = b.CurrentHolderType, id = b.CurrentHolderId },
            b.CreatedAt,
        }));
    }

    [HttpPost("graph/farm-network")]
    public async Task<IActionResult> FarmNetwork([FromBody] FarmNetworkRequest request, CancellationToken ct)
    {
        var farms = await db.Farms
            .Where(f => request.FarmIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.OwnerIin, f.BankBin, f.ElevatorContractId })
            .ToListAsync(ct);

        var nodes = farms.Select(f => new { id = f.Id, name = f.Name }).ToList();
        var edges = new List<object>();

        // edges by shared owner IIN
        var byIin = farms.Where(f => f.OwnerIin != null).GroupBy(f => f.OwnerIin!);
        foreach (var group in byIin)
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    edges.Add(new { from = list[i].Id, to = list[j].Id, edge_type = "owner" });
        }

        // edges by shared bank BIN
        var byBank = farms.Where(f => f.BankBin != null).GroupBy(f => f.BankBin!);
        foreach (var group in byBank)
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    edges.Add(new { from = list[i].Id, to = list[j].Id, edge_type = "bank" });
        }

        // edges by shared elevator contract
        var byElevator = farms.Where(f => f.ElevatorContractId != null).GroupBy(f => f.ElevatorContractId!);
        foreach (var group in byElevator)
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    edges.Add(new { from = list[i].Id, to = list[j].Id, edge_type = "elevator" });
        }

        return Ok(new { nodes, edges });
    }

    private static string GenerateBatchCode(Guid farmId, string cropType)
    {
        var prefix = cropType.Length >= 3 ? cropType[..3].ToUpperInvariant() : cropType.ToUpperInvariant();
        var suffix = farmId.ToString("N")[..8].ToUpperInvariant();
        return $"{prefix}-{suffix}-{DateTime.UtcNow:yyyyMMdd}";
    }
}

public class CreateBatchRequest
{
    [JsonPropertyName("farm_id")]
    public Guid FarmId { get; set; }

    [JsonPropertyName("crop_type")]
    public string CropType { get; set; } = null!;

    [JsonPropertyName("weight_kg")]
    public decimal WeightKg { get; set; }

    [JsonPropertyName("harvest_date")]
    public DateOnly? HarvestDate { get; set; }
}

public class MoveBatchRequest
{
    [JsonPropertyName("to_node_type")]
    public string ToNodeType { get; set; } = null!;

    [JsonPropertyName("to_node_id")]
    public string ToNodeId { get; set; } = null!;

    [JsonPropertyName("weight_kg")]
    public decimal WeightKg { get; set; }

    [JsonPropertyName("transferred_at")]
    public DateTime? TransferredAt { get; set; }

    public string? Notes { get; set; }
}

public class FarmNetworkRequest
{
    [JsonPropertyName("farm_ids")]
    public Guid[] FarmIds { get; set; } = [];
}
