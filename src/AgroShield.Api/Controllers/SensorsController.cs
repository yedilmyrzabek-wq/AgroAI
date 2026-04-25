using AgroShield.Api.Filters;
using AgroShield.Application.DTOs.Sensors;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/sensors")]
public class SensorsController(ISensorService sensors) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [HttpPost("reading")]
    [ApiKey]
    public async Task<IActionResult> SaveReading(CancellationToken ct)
    {
        CreateSensorReadingDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<CreateSensorReadingDto>(Request.Body, SnakeOptions, ct);
        }
        catch
        {
            return BadRequest(new { error = "invalid_body", message = "Cannot parse request body" });
        }

        if (dto is null || string.IsNullOrEmpty(dto.DeviceId))
            return BadRequest(new { error = "invalid_body", message = "device_id is required" });

        try
        {
            var result = await sensors.SaveReadingAsync(dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
    }

    [HttpPost("rfid-scan")]
    [ApiKey]
    public async Task<IActionResult> RfidScan(CancellationToken ct)
    {
        RfidScanDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<RfidScanDto>(Request.Body, SnakeOptions, ct);
        }
        catch
        {
            return BadRequest(new { error = "invalid_body", message = "Cannot parse request body" });
        }

        if (dto is null) return BadRequest(new { error = "invalid_body", message = "Body is required" });

        await sensors.SaveRfidScanAsync(dto, ct);
        return Ok(new { status = "ok" });
    }

    [HttpGet("{farmId:guid}/latest")]
    [Authorize]
    public async Task<IActionResult> GetLatest(Guid farmId, CancellationToken ct)
    {
        var reading = await sensors.GetLatestAsync(farmId, ct);
        return reading is null ? NotFound(new { error = "not_found", message = "No readings found" }) : Ok(reading);
    }

    [HttpGet("{farmId:guid}/history")]
    [Authorize]
    public async Task<IActionResult> GetHistory(Guid farmId, [FromQuery] string period = "24h", CancellationToken ct = default)
    {
        var readings = await sensors.GetHistoryAsync(farmId, period, ct);
        return Ok(readings);
    }
}
