using AgroShield.Api.Filters;
using AgroShield.Application.DTOs.Subsidies;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/subsidies")]
[InternalApiKey]
public class InternalSubsidyController(
    AppDbContext db,
    ISupplyChainService supplyChain,
    IHttpClientFactory factory,
    ILogger<InternalSubsidyController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly (int Order, decimal Percent, string Condition)[] DefaultPlan =
    [
        (1, 30m, "registered"),
        (2, 30m, "sowing_confirmed"),
        (3, 20m, "fertilizer_applied"),
        (4, 20m, "harvest_recorded"),
    ];

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubsidyTrancheDto dto, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == dto.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var farmerId = dto.FarmerId ?? farm.OwnerId;
        var cropType = !string.IsNullOrWhiteSpace(dto.CropType) ? dto.CropType : farm.CropType;
        var now = DateTime.UtcNow;

        var subsidy = new Subsidy
        {
            Id = Guid.NewGuid(),
            FarmId = dto.FarmId,
            FarmerId = farmerId,
            CropType = cropType,
            Amount = dto.AmountKzt,
            DeclaredArea = farm.AreaHectares,
            Purpose = $"Tranche subsidy ({cropType})",
            Status = SubsidyStatus.Approved,
            WorkflowStatus = "in_progress",
            SubmittedAt = now,
        };
        subsidy.Tranches = DefaultPlan.Select(p => new SubsidyTranche
        {
            Id = Guid.NewGuid(),
            SubsidyId = subsidy.Id,
            Order = p.Order,
            PercentOfTotal = p.Percent,
            AmountKzt = Math.Round(dto.AmountKzt * p.Percent / 100m, 2, MidpointRounding.AwayFromZero),
            Status = p.Order == 1 ? "released" : "pending",
            UnlockCondition = p.Condition,
            ReleasedAt = p.Order == 1 ? now : null,
            CreatedAt = now,
        }).ToList();

        db.Subsidies.Add(subsidy);
        await db.SaveChangesAsync(ct);

        var actor = ResolveActorId();

        await supplyChain.AppendAsync(
            BatchId(subsidy.Id),
            "registered",
            new
            {
                subsidy_id = subsidy.Id,
                amount_kzt = dto.AmountKzt,
                crop_type = cropType,
                farm_id = dto.FarmId,
                farmer_id = farmerId,
            },
            actor, ct);

        await supplyChain.AppendAsync(
            BatchId(subsidy.Id),
            "tranche_released",
            new
            {
                tranche_order = 1,
                amount_kzt = subsidy.Tranches.First(t => t.Order == 1).AmountKzt,
                unlock_condition = "registered",
            },
            actor, ct);

        return Ok(MapDetails(subsidy, farm.Name));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var subsidy = await db.Subsidies
            .Include(s => s.Tranches)
            .Include(s => s.Farm)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subsidy is null)
            return NotFound(new { error = "not_found" });
        return Ok(MapDetails(subsidy, subsidy.Farm.Name));
    }

    [HttpGet("by-farm/{farmId:guid}")]
    public async Task<IActionResult> ListByFarm(Guid farmId, CancellationToken ct)
    {
        var rows = await db.Subsidies
            .Where(s => s.FarmId == farmId)
            .Include(s => s.Tranches)
            .Include(s => s.Farm)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync(ct);
        return Ok(rows.Select(s => MapDetails(s, s.Farm.Name)));
    }

    [HttpPost("{id:guid}/record-fertilizer-application")]
    public async Task<IActionResult> RecordFertilizer(Guid id, [FromBody] RecordFertilizerDto dto, CancellationToken ct)
    {
        var subsidy = await db.Subsidies
            .Include(s => s.Tranches)
            .Include(s => s.Farm)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subsidy is null) return NotFound(new { error = "not_found" });

        var farm = subsidy.Farm;
        var area = (double)farm.AreaHectares;
        var expectedGrowth = dto.ExpectedGrowthPct ?? 15m;

        JsonElement mlResult;
        try
        {
            var fert = factory.CreateClient("FertilizerAdvisor");
            var resp = await fert.PostAsJsonAsync("/verify-application", new
            {
                farm_id = farm.Id.ToString(),
                lat = farm.Lat,
                lng = farm.Lng,
                area_hectares = area,
                applied_at = dto.AppliedAt.ToString("yyyy-MM-dd"),
                expected_growth_pct = (double)expectedGrowth,
            }, SnakeOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("FertilizerAdvisor /verify-application failed: {Status}", resp.StatusCode);
                mlResult = JsonSerializer.Deserialize<JsonElement>(
                    """{"verdict":"unverifiable","explanation_ru":"ML сервис недоступен","is_mock":true}""");
            }
            else
            {
                mlResult = await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FertilizerAdvisor verify-application exception");
            mlResult = JsonSerializer.Deserialize<JsonElement>(
                $$"""{"verdict":"unverifiable","explanation_ru":"ML сервис недоступен: {{ex.GetType().Name}}","is_mock":true}""");
        }

        var actor = ResolveActorId();
        await supplyChain.AppendAsync(
            BatchId(subsidy.Id),
            "fertilizer_application_recorded",
            new
            {
                applied_at = dto.AppliedAt,
                expected_growth_pct = expectedGrowth,
                ml_result = mlResult,
            },
            actor, ct);

        // Pre-fill release evidence on the fertilizer_applied tranche so disbursement job can act
        var tranche = subsidy.Tranches.FirstOrDefault(t => t.UnlockCondition == "fertilizer_applied");
        if (tranche is not null)
        {
            tranche.ReleaseEvidenceJson = JsonSerializer.Serialize(new
            {
                applied_at = dto.AppliedAt,
                ml_verdict = mlResult.TryGetProperty("verdict", out var v) ? v.GetString() : null,
                ml_growth_pct = mlResult.TryGetProperty("growth_pct", out var g) && g.ValueKind == JsonValueKind.Number ? (double?)g.GetDouble() : null,
                source = "subsidy.record-fertilizer-application",
            }, SnakeOpts);
            await db.SaveChangesAsync(ct);
        }

        return Ok(mlResult);
    }

    [HttpPost("{id:guid}/record-harvest")]
    public async Task<IActionResult> RecordHarvest(Guid id, [FromBody] RecordHarvestDto dto, CancellationToken ct)
    {
        var subsidy = await db.Subsidies
            .Include(s => s.Tranches)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subsidy is null) return NotFound(new { error = "not_found" });

        var actor = ResolveActorId();
        var record = await supplyChain.AppendAsync(
            BatchId(subsidy.Id),
            "harvest_recorded",
            new
            {
                harvest_kg = dto.HarvestKg,
                harvested_at = dto.HarvestedAt ?? DateTime.UtcNow,
                notes = dto.Notes,
            },
            actor, ct);

        var tranche = subsidy.Tranches.FirstOrDefault(t => t.UnlockCondition == "harvest_recorded");
        if (tranche is not null)
        {
            tranche.ReleaseEvidenceJson = JsonSerializer.Serialize(new
            {
                harvest_kg = dto.HarvestKg,
                source = "subsidy.record-harvest",
                ledger_hash = record.EntryHash,
            }, SnakeOpts);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { recorded = true, ledger_hash = record.EntryHash });
    }

    [HttpGet("{id:guid}/ledger")]
    public async Task<IActionResult> GetLedger(Guid id, CancellationToken ct)
    {
        var subsidy = await db.Subsidies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subsidy is null) return NotFound(new { error = "not_found" });
        var ledger = await supplyChain.GetLedgerAsync(BatchId(id), ct);
        return Ok(ledger);
    }

    [HttpGet("{id:guid}/verify-integrity")]
    public async Task<IActionResult> VerifyIntegrity(Guid id, CancellationToken ct)
    {
        var subsidy = await db.Subsidies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subsidy is null) return NotFound(new { error = "not_found" });
        var result = await supplyChain.VerifyAsync(BatchId(id), ct);
        return Ok(new { is_valid = result.IsValid, length = result.Length, broken_at = result.BrokenAtTimestamp });
    }

    private Guid? ResolveActorId()
    {
        if (Request.Headers.TryGetValue("X-User-Id", out var raw) && Guid.TryParse(raw, out var g))
            return g;
        return null;
    }

    private static string BatchId(Guid subsidyId) => $"subsidy-{subsidyId:N}";

    private static SubsidyDetailsDto MapDetails(Subsidy s, string farmName) =>
        new(
            s.Id,
            s.FarmId,
            farmName,
            s.FarmerId,
            s.Amount,
            s.CropType ?? "",
            s.WorkflowStatus,
            s.SubmittedAt,
            s.CompletedAt,
            s.Tranches
                .OrderBy(t => t.Order)
                .Select(t => new SubsidyTrancheDto(
                    t.Id, t.SubsidyId, t.Order, t.PercentOfTotal, t.AmountKzt,
                    t.Status, t.UnlockCondition, t.ReleasedAt, t.ReleaseEvidenceJson))
                .ToList());
}
