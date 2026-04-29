using AgroShield.Api.Filters;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/farms")]
[InternalApiKey]
public class InternalFarmsController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalFarmsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpPost("{id:guid}/predict-yield")]
    public async Task<IActionResult> PredictYield(Guid id, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (farm is null)
            return NotFound(new { error = "not_found", message = "Farm not found" });

        var region = (farm.Region?.Contains("Север") == true || farm.Region?.Contains("Костан") == true)
            ? "north_kz" : null;

        try
        {
            var client = factory.CreateClient("YieldPredictor");
            var body = new
            {
                farm_id = farm.Id.ToString(),
                lat = farm.Lat,
                lng = farm.Lng,
                region,
                ndvi_mean = (double?)farm.NdviMean ?? 0.5,
                area_hectares = (double)farm.AreaHectares,
                crop_type = (farm.CropType ?? "wheat").ToLowerInvariant(),
                avg_temp = 18,
                avg_humidity = 55,
                avg_light = 800,
            };
            var resp = await client.PostAsJsonAsync("/predict-yield", body, SnakeOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                logger.LogWarning("YieldPredictor returned {Status}: {Body}", resp.StatusCode, err);
                return StatusCode(502, new { error = "ml_error", message = err });
            }
            return Ok(await resp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YieldPredictor unavailable");
            return StatusCode(503, new { error = "ml_unavailable", message = ex.GetType().Name, is_mock = true });
        }
    }
}
