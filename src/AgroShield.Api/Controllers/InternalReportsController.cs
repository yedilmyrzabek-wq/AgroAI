using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/reports")]
[InternalApiKey]
public class InternalReportsController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalReportsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpGet("weekly/{userId:guid}")]
    public async Task<IActionResult> GetWeeklyReport(Guid userId, [FromQuery] DateOnly? weekStart = null, CancellationToken ct = default)
    {
        var query = db.WeeklyReports.Where(r => r.UserId == userId);
        if (weekStart.HasValue)
            query = query.Where(r => r.WeekStart == weekStart.Value);

        var report = await query.OrderByDescending(r => r.WeekStart).FirstOrDefaultAsync(ct);
        if (report is null)
            return NotFound(new { error = "not_found", message = "No report found" });

        return Ok(MapReport(report));
    }

    [HttpGet("weekly/list/{userId:guid}")]
    public async Task<IActionResult> ListReports(Guid userId, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var reports = await db.WeeklyReports
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.WeekStart)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(reports.Select(MapReport));
    }

    [HttpPost("weekly/generate")]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null)
            return NotFound(new { error = "not_found", message = "User not found" });

        var farms = await db.Farms
            .Where(f => request.FarmIds.Contains(f.Id))
            .ToListAsync(ct);

        var anomalies = await db.Anomalies
            .Where(a => request.FarmIds.Contains(a.FarmId) && a.DetectedAt >= DateTime.UtcNow.AddDays(-7))
            .CountAsync(ct);

        var diagnoses = await db.PlantDiagnoses
            .Where(d => request.FarmIds.Contains(d.FarmId) && d.CreatedAt >= DateTime.UtcNow.AddDays(-7))
            .CountAsync(ct);

        var avgNdvi = farms.Where(f => f.NdviMean.HasValue).Select(f => f.NdviMean!.Value).DefaultIfEmpty(0).Average();

        var statsJson = JsonSerializer.Serialize(new
        {
            farms_count = farms.Count,
            anomalies_week = anomalies,
            diagnoses_week = diagnoses,
            avg_ndvi = avgNdvi,
            farms = farms.Select(f => new { f.Id, f.Name, f.NdviMean, f.RiskScore }),
        }, SnakeOpts);

        string markdown;
        try
        {
            var client = factory.CreateClient("AiAssistant");
            var payload = new { user_id = request.UserId, farm_ids = request.FarmIds, stats_json = statsJson };
            var response = await client.PostAsJsonAsync("/generate-weekly-report", payload, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            markdown = result.TryGetProperty("markdown", out var md) ? md.GetString()! : result.GetRawText();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiAssistant generate-weekly-report failed, using mock");
            markdown = BuildMockReport(farms, anomalies, diagnoses, avgNdvi);
        }

        var now = DateTime.UtcNow;
        var weekStart = DateOnly.FromDateTime(now.AddDays(-(int)now.DayOfWeek + 1));
        var weekEnd = weekStart.AddDays(6);

        var report = new WeeklyReport
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            FarmIds = request.FarmIds,
            ReportMarkdown = markdown,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            CreatedAt = now,
        };

        db.WeeklyReports.Add(report);
        await db.SaveChangesAsync(ct);

        // send to Telegram if subscribed
        var sub = await db.NotificationSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.NotificationType == "weekly_report" && s.Enabled, ct);

        if (sub is not null && user.TelegramChatId.HasValue)
        {
            try
            {
                var tgClient = factory.CreateClient("TelegramBot");
                var payload = new { telegram_chat_id = user.TelegramChatId.Value, message = markdown, parse_mode = "Markdown" };
                var tgResponse = await tgClient.PostAsJsonAsync("/send", payload, SnakeOpts, ct);
                if (tgResponse.IsSuccessStatusCode)
                {
                    report.DeliveredAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deliver weekly report via Telegram");
            }
        }

        return Ok(MapReport(report));
    }

    private static object MapReport(WeeklyReport r) => new
    {
        r.Id,
        r.UserId,
        r.FarmIds,
        r.ReportMarkdown,
        r.WeekStart,
        r.WeekEnd,
        r.DeliveredAt,
        r.CreatedAt,
    };

    private static string BuildMockReport(
        List<Domain.Entities.Farm> farms,
        int anomalies, int diagnoses, decimal avgNdvi) =>
        $"""
        # Еженедельный отчёт AgroShield

        **Период:** {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7))} — {DateOnly.FromDateTime(DateTime.UtcNow)}

        ## Фермы под мониторингом: {farms.Count}
        {string.Join("\n", farms.Select(f => $"- **{f.Name}** | NDVI: {f.NdviMean:F3} | Риск: {f.RiskScore}"))}

        ## Сводка за неделю
        - Аномалий обнаружено: **{anomalies}**
        - Диагнозов растений: **{diagnoses}**
        - Средний NDVI: **{avgNdvi:F3}**

        *Отчёт сгенерирован в mock-режиме (AI-сервис недоступен)*
        """;
}

public class GenerateReportRequest
{
    public Guid UserId { get; set; }
    public Guid[] FarmIds { get; set; } = [];
}
