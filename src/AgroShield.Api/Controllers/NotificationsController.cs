using AgroShield.Application.Auth;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(AppDbContext db, ICurrentUserAccessor user) : ControllerBase
{
    private static readonly string[] ValidTypes =
    [
        "ndvi_drop", "fire", "anomaly", "weekly_report",
        "daily_digest", "supply_chain_freeze",
        "livestock_anomaly", "supply_chain_anomaly",
    ];

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions(CancellationToken ct)
    {
        var existing = await db.NotificationSubscriptions
            .Where(s => s.UserId == user.UserId)
            .ToDictionaryAsync(s => s.NotificationType, s => s.Enabled, ct);

        var items = ValidTypes.Select(t => new
        {
            type = t,
            enabled = existing.TryGetValue(t, out var en) && en,
        });
        return Ok(items);
    }

    [HttpPatch("subscriptions/{type}")]
    public async Task<IActionResult> UpdateSubscription(string type, [FromBody] PatchSubscriptionRequest body, CancellationToken ct)
    {
        if (!ValidTypes.Contains(type))
            return BadRequest(new { error = "validation_error", message = $"Unknown notification type: {type}" });

        var sub = await db.NotificationSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == user.UserId && s.NotificationType == type, ct);

        if (sub is null)
        {
            sub = new NotificationSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                NotificationType = type,
                Enabled = body.Enabled,
                CreatedAt = DateTime.UtcNow,
            };
            db.NotificationSubscriptions.Add(sub);
        }
        else
        {
            sub.Enabled = body.Enabled;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { type, enabled = body.Enabled });
    }

    public record PatchSubscriptionRequest(bool Enabled);
}
