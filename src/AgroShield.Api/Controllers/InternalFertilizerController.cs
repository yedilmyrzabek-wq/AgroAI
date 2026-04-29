using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/fertilizer")]
[InternalApiKey]
public class InternalFertilizerController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalFertilizerController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpGet("{farmId:guid}/recommendation")]
    public async Task<IActionResult> GetRecommendation(
        Guid farmId,
        [FromQuery] bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        if (!forceRefresh)
        {
            var cached = await db.FertilizerRecommendations
                .Where(r => r.FarmId == farmId && r.CreatedAt > DateTime.UtcNow.AddDays(-7))
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (cached is not null)
                return Ok(MapRecommendation(cached));
        }

        // Call fertilizer-advisor ML
        FertilizerRecommendation rec;
        try
        {
            var client = factory.CreateClient("FertilizerAdvisor");
            var payload = new
            {
                farm_id = farmId,
                crop_type = farm.CropType,
                area_ha = farm.AreaHectares,
                ndvi_mean = farm.NdviMean,
                region = farm.Region,
            };
            var response = await client.PostAsJsonAsync("/recommend", payload, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));

            rec = new FertilizerRecommendation
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                NKgPerHa = GetDecimal(result, "n_kg_per_ha"),
                PKgPerHa = GetDecimal(result, "p_kg_per_ha"),
                KKgPerHa = GetDecimal(result, "k_kg_per_ha"),
                TotalKg = GetDecimal(result, "total_kg"),
                EstimatedCostKzt = GetDecimal(result, "estimated_cost_kzt"),
                ExpectedYieldIncreasePct = GetDecimal(result, "expected_yield_increase_pct"),
                ApplicationWindows = result.TryGetProperty("application_windows", out var aw) ? aw.GetRawText() : null,
                ExplanationRu = result.TryGetProperty("explanation_ru", out var exp) ? exp.GetString() : null,
                ModelVersion = result.TryGetProperty("model_version", out var mv) ? mv.GetString() : null,
                CreatedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FertilizerAdvisor unavailable, using mock");
            rec = new FertilizerRecommendation
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                NKgPerHa = 120,
                PKgPerHa = 60,
                KKgPerHa = 40,
                TotalKg = (120 + 60 + 40) * farm.AreaHectares,
                EstimatedCostKzt = 250000,
                ExpectedYieldIncreasePct = 15,
                ExplanationRu = "Мок-рекомендация (сервис недоступен)",
                ModelVersion = "mock",
                CreatedAt = DateTime.UtcNow,
            };
        }

        db.FertilizerRecommendations.Add(rec);
        await db.SaveChangesAsync(ct);

        return Ok(MapRecommendation(rec));
    }

    [HttpPost("verify-application")]
    public async Task<IActionResult> VerifyApplication([FromBody] VerifyApplicationRequest request, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == request.FarmId, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var expectedGrowth = request.ExpectedGrowthPct ?? 15m;

        try
        {
            var client = factory.CreateClient("FertilizerAdvisor");
            var resp = await client.PostAsJsonAsync("/verify-application", new
            {
                farm_id = farm.Id.ToString(),
                lat = farm.Lat,
                lng = farm.Lng,
                area_hectares = (double)farm.AreaHectares,
                applied_at = request.AppliedAt.ToString("yyyy-MM-dd"),
                expected_growth_pct = (double)expectedGrowth,
            }, SnakeOpts, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                logger.LogWarning("FertilizerAdvisor verify-application returned {Status}: {Body}", resp.StatusCode, body);
                return StatusCode(502, new { error = "ml_error", message = $"FertilizerAdvisor returned {(int)resp.StatusCode}" });
            }

            return Ok(await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FertilizerAdvisor verify-application unavailable");
            return StatusCode(503, new
            {
                error = "ml_unavailable",
                message = "FertilizerAdvisor service unreachable",
                verdict = "unverifiable",
                applied_at = request.AppliedAt.ToString("yyyy-MM-dd"),
                checked_at = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                explanation_ru = $"Сервис проверки удобрений недоступен: {ex.GetType().Name}",
                is_mock = true,
            });
        }
    }

    [HttpPost("optimize-budget")]
    public async Task<IActionResult> OptimizeBudget([FromBody] OptimizeBudgetRequest request, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient("FertilizerAdvisor");
            var response = await client.PostAsJsonAsync("/optimize-budget", request, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            return Ok(await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FertilizerAdvisor optimize-budget failed");
            return Ok(new { _mock = true, message = "Service unavailable", optimized_farms = Array.Empty<object>() });
        }
    }

    [HttpGet("crop-defaults/{cropType}")]
    public async Task<IActionResult> GetCropDefaults(string cropType, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient("FertilizerAdvisor");
            var response = await client.GetAsync($"/crop-defaults/{cropType}", ct);
            response.EnsureSuccessStatusCode();
            return Ok(await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FertilizerAdvisor crop-defaults failed");
            return Ok(new { _mock = true, crop_type = cropType, n_default = 100, p_default = 50, k_default = 40 });
        }
    }

    private static object MapRecommendation(FertilizerRecommendation r) => new
    {
        r.Id,
        r.FarmId,
        n_kg_per_ha = r.NKgPerHa,
        p_kg_per_ha = r.PKgPerHa,
        k_kg_per_ha = r.KKgPerHa,
        total_kg = r.TotalKg,
        estimated_cost_kzt = r.EstimatedCostKzt,
        expected_yield_increase_pct = r.ExpectedYieldIncreasePct,
        application_windows = r.ApplicationWindows,
        explanation_ru = r.ExplanationRu,
        model_version = r.ModelVersion,
        created_at = r.CreatedAt,
    };

    private static decimal? GetDecimal(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
}

public class OptimizeBudgetRequest
{
    public decimal BudgetKzt { get; set; }
    public Guid[] FarmIds { get; set; } = [];
}

public class VerifyApplicationRequest
{
    public Guid FarmId { get; set; }
    public DateTime AppliedAt { get; set; }
    public decimal? ExpectedGrowthPct { get; set; }
}
