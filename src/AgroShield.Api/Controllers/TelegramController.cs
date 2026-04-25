using AgroShield.Application.Auth;
using AgroShield.Application.Services;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/telegram")]
[Authorize]
public class TelegramController(
    ITelegramLinkService telegramLink,
    AppDbContext db,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost("generate-code")]
    [Authorize(Roles = "Farmer")]
    public async Task<IActionResult> GenerateCode()
    {
        var code = await telegramLink.GenerateCodeAsync(currentUser.UserId);
        return Ok(new { code, expires_at = DateTime.UtcNow.AddMinutes(10) });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var profile = await db.Profiles.FindAsync([currentUser.UserId], ct);
        if (profile is null)
            return Ok(new { linked = false, farm_name = (string?)null, telegram_chat_id = (long?)null });

        var farmName = profile.FarmId.HasValue
            ? await db.Farms.Where(f => f.Id == profile.FarmId.Value).Select(f => f.Name).FirstOrDefaultAsync(ct)
            : null;

        return Ok(new
        {
            linked = profile.TelegramChatId.HasValue,
            farm_name = farmName,
            telegram_chat_id = profile.TelegramChatId,
        });
    }
}
