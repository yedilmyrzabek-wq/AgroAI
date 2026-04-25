using AgroShield.Api.Filters;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/plants")]
[InternalApiKey]
public class InternalPlantsController(
    IMLProxyService ml,
    IStorageService storage,
    AppDbContext db) : ControllerBase
{
    [HttpPost("diagnose-by-chat")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> DiagnoseByChat(
        [FromForm] DiagnoseByChataRequest request,
        CancellationToken ct)
    {
        var file = request.File;
        var chatId = request.ChatId;

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);

        if (user is null)
            return NotFound(new { error = "not_found", message = "Chat not linked", details = (object?)null });

        var farmId = await db.Farms
            .Where(f => f.OwnerId == user.Id)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);

        await using var stream = file.OpenReadStream();
        var result = await ml.DiagnosePlantAsync(stream, file.FileName, farmId, ct);

        if (farmId.HasValue)
        {
            var imageUrl = "";
            try
            {
                var key = $"diagnoses/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                await using var uploadStream = file.OpenReadStream();
                imageUrl = await storage.UploadAsync(uploadStream, key, file.ContentType);
            }
            catch { /* storage optional */ }

            db.PlantDiagnoses.Add(new PlantDiagnosis
            {
                Id = Guid.NewGuid(),
                FarmId = farmId.Value,
                UserId = user.Id,
                ImageUrl = imageUrl,
                Disease = result.Disease,
                DiseaseRu = result.DiseaseRu,
                Confidence = result.Confidence,
                Severity = result.Severity,
                IsHealthy = result.IsHealthy,
                Recommendation = result.Recommendation,
                ModelVersion = result.ModelVersion,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            disease = result.Disease,
            disease_ru = result.DiseaseRu,
            confidence = result.Confidence,
            recommendation = result.Recommendation,
            severity = result.Severity,
            is_healthy = result.IsHealthy,
            is_uncertain = result.IsUncertain,
            alternatives = result.Alternatives.Select(a => new
            {
                disease = a.Disease,
                disease_ru = a.DiseaseRu,
                confidence = a.Confidence,
            }),
            model_version = result.ModelVersion,
        });
    }
}

public class DiagnoseByChataRequest
{
    public IFormFile File { get; set; } = null!;
    public long ChatId { get; set; }
}
