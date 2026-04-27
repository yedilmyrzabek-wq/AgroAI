using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/notifications")]
[InternalApiKey]
public class InternalNotificationsController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalNotificationsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly string[] ValidTypes =
    [
        "ndvi_drop", "fire", "anomaly", "weekly_report",
        "livestock_anomaly", "supply_chain_anomaly"
    ];

    [HttpGet("subscriptions/{userId:guid}")]
    public async Task<IActionResult> GetSubscriptions(Guid userId, CancellationToken ct)
    {
        var existing = await db.NotificationSubscriptions
            .Where(s => s.UserId == userId)
            .ToDictionaryAsync(s => s.NotificationType, s => s.Enabled, ct);

        return Ok(ValidTypes.Select(t => new
        {
            type = t,
            enabled = existing.TryGetValue(t, out var en) && en,
        }));
    }

    [HttpPut("subscriptions/{userId:guid}/{type}")]
    public async Task<IActionResult> ToggleSubscription(Guid userId, string type, [FromBody] ToggleRequest request, CancellationToken ct)
    {
        if (!ValidTypes.Contains(type))
            return BadRequest(new { error = "invalid_type", message = $"Unknown notification type: {type}" });

        var user = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!user)
            return NotFound(new { error = "not_found", message = "User not found" });

        var sub = await db.NotificationSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.NotificationType == type, ct);

        if (sub is null)
        {
            sub = new NotificationSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NotificationType = type,
                Enabled = request.Enabled,
                CreatedAt = DateTime.UtcNow,
            };
            db.NotificationSubscriptions.Add(sub);
        }
        else
        {
            sub.Enabled = request.Enabled;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { type, enabled = request.Enabled });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request, CancellationToken ct)
    {
        var sub = await db.NotificationSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.NotificationType == request.Type && s.Enabled, ct);

        if (sub is null)
            return Ok(new { sent = false, reason = "not_subscribed" });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user?.TelegramChatId is null)
            return Ok(new { sent = false, reason = "no_telegram" });

        try
        {
            var client = factory.CreateClient("TelegramBot");
            var payload = new
            {
                telegram_chat_id = user.TelegramChatId.Value,
                message = request.Message,
                parse_mode = request.ParseMode ?? "Markdown",
            };
            var response = await client.PostAsJsonAsync("/send", payload, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            return Ok(new { sent = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TelegramBot send failed for user {UserId}", request.UserId);
            return Ok(new { sent = false, reason = "telegram_error", _mock = true });
        }
    }
}

public class ToggleRequest
{
    public bool Enabled { get; set; }
}

public class SendNotificationRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? ParseMode { get; set; }
}
