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
    IStorageService storage,
    AppDbContext db,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost("diagnose")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Diagnose([FromForm] DiagnoseRequest request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        var result = await ml.DiagnosePlantAsync(stream, request.File.FileName, request.FarmId, ct);
        var farmId = request.FarmId;

        if (farmId.HasValue)
        {
            var key = $"diagnoses/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}{Path.GetExtension(request.File.FileName)}";
            var imageUrl = "";
            try
            {
                await using var uploadStream = request.File.OpenReadStream();
                imageUrl = await storage.UploadAsync(uploadStream, key, request.File.ContentType);
            }
            catch { /* storage optional — don't fail diagnosis */ }

            var diagnosis = new PlantDiagnosis
            {
                Id = Guid.NewGuid(),
                FarmId = farmId.Value,
                UserId = currentUser.UserId,
                ImageUrl = imageUrl,
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

public class DiagnoseRequest
{
    public IFormFile File { get; set; } = null!;
    public Guid? FarmId { get; set; }
}
