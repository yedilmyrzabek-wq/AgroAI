using AgroShield.Application.DTOs.Farms;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/farms")]
[Authorize]
public class FarmsController(
    IFarmService farms,
    IHttpClientFactory factory,
    IMemoryCache cache,
    IClimateRiskService climateRisk) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] FarmFilterDto filter, CancellationToken ct) =>
        Ok(await farms.GetAllAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try { return Ok(await farms.GetByIdAsync(id, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Roles = "Inspector,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateFarmDto dto, CancellationToken ct)
    {
        var farm = await farms.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = farm.Id }, farm);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmDto dto, CancellationToken ct)
    {
        try { return Ok(await farms.UpdateAsync(id, dto, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await farms.DeleteAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpPost("{id:guid}/refresh-ndvi")]
    [Authorize(Roles = "Inspector,Admin")]
    public async Task<IActionResult> RefreshNdvi(Guid id, CancellationToken ct)
    {
        try
        {
            await farms.RefreshNdviAsync(id, ct);
            return Ok(await farms.GetByIdAsync(id, ct));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpGet("{id:guid}/multi-index")]
    public async Task<IActionResult> GetMultiIndex(Guid id, CancellationToken ct)
    {
        var cacheKey = $"multi-index:{id}";
        if (cache.TryGetValue(cacheKey, out var cached))
            return Ok(cached);

        try
        {
            var client = factory.CreateClient("SatelliteNdvi");
            var response = await client.GetAsync($"/multi-index/{id}", ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            return Ok(result);
        }
        catch
        {
            return Ok(new { _mock = true, farm_id = id, ndvi = 0.45, evi = 0.38, savi = 0.42, cached_at = DateTime.UtcNow });
        }
    }

    [HttpGet("{id:guid}/climate-risk")]
    public async Task<IActionResult> GetClimateRisk(Guid id, CancellationToken ct) =>
        Ok(await climateRisk.GetForFarmAsync(id, ct));

    [HttpGet("{id:guid}/ndvi-time-series")]
    public async Task<IActionResult> GetNdviTimeSeries(Guid id, CancellationToken ct)
    {
        var cacheKey = $"ndvi-ts:{id}";
        if (cache.TryGetValue(cacheKey, out var cached))
            return Ok(cached);

        try
        {
            var client = factory.CreateClient("SatelliteNdvi");
            var response = await client.GetAsync($"/time-series/{id}", ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            return Ok(result);
        }
        catch
        {
            return Ok(new { _mock = true, farm_id = id, series = Array.Empty<object>() });
        }
    }
}
