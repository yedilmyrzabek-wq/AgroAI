using AgroShield.Api.Filters;
using AgroShield.Application.Services;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/telegram")]
[InternalApiKey]
public class InternalTelegramController(
    ITelegramLinkService telegramLink,
    AppDbContext db) : ControllerBase
{
    [HttpPost("link")]
    public async Task<IActionResult> Link([FromBody] TelegramLinkRequestDto dto, CancellationToken ct)
    {
        var linked = await telegramLink.LinkAsync(dto.Code, dto.TelegramChatId);
        if (!linked)
            return BadRequest(new { error = "invalid_code", message = "Code not found or expired" });

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.TelegramChatId == dto.TelegramChatId, ct);

        string? farmName = null;
        if (user is not null)
            farmName = await db.Farms.Where(f => f.OwnerId == user.Id)
                .Select(f => f.Name).FirstOrDefaultAsync(ct);

        return Ok(new { linked = true, farm_name = farmName });
    }

    [HttpGet("farm-status")]
    public async Task<IActionResult> FarmStatus([FromQuery] long chat_id, CancellationToken ct)
    {
        var status = await telegramLink.GetFarmStatusByChatIdAsync(chat_id);
        return Ok(status ?? (object)new { linked = false });
    }
}

public class TelegramLinkRequestDto
{
    public string Code { get; set; } = null!;
    public long TelegramChatId { get; set; }
}
