using AgroShield.Application.Auth;
using AgroShield.Application.DTOs.Plants;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/plants")]
[Authorize]
public class PlantsController(
    IMLProxyService ml,
    AppDbContext db,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost("diagnose")]
    public async Task<IActionResult> Diagnose(
        [FromForm] IFormFile file,
        [FromForm] Guid? farmId,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await ml.DiagnosePlantAsync(stream, file.FileName, farmId, ct);

        if (farmId.HasValue)
        {
            var diagnosis = new PlantDiagnosis
            {
                Id = Guid.NewGuid(),
                FarmId = farmId.Value,
                UserId = currentUser.UserId,
                ImageUrl = "",
                Disease = result.Disease,
                DiseaseRu = result.DiseaseRu,
                Confidence = result.Confidence,
                Severity = result.Severity,
                IsHealthy = result.IsHealthy,
                Recommendation = result.Recommendation,
                ModelVersion = result.ModelVersion,
                CreatedAt = DateTime.UtcNow,
            };
            db.PlantDiagnoses.Add(diagnosis);
            await db.SaveChangesAsync(ct);
            return Ok(ToDto(diagnosis));
        }

        return Ok(result);
    }

    [HttpGet("diagnoses")]
    public async Task<IActionResult> GetDiagnoses([FromQuery] PlantDiagnosisFilterDto filter, CancellationToken ct)
    {
        var q = db.PlantDiagnoses.AsQueryable();
        if (filter.FarmId.HasValue) q = q.Where(d => d.FarmId == filter.FarmId.Value);
        if (!string.IsNullOrEmpty(filter.Severity)) q = q.Where(d => d.Severity == filter.Severity);

        var items = await q
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(filter.Limit, 1, 100))
            .Select(d => ToDto(d))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("diagnoses/today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var items = await db.PlantDiagnoses
            .Where(d => d.CreatedAt >= today)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => ToDto(d))
            .ToListAsync(ct);

        return Ok(items);
    }

    private static PlantDiagnosisDto ToDto(PlantDiagnosis d) =>
        new(d.Id, d.FarmId, d.UserId, d.ImageUrl, d.Disease, d.DiseaseRu,
            d.Confidence, d.Severity, d.IsHealthy, d.Recommendation, d.ModelVersion, d.CreatedAt);
}
