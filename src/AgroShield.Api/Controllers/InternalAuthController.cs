using AgroShield.Api.Filters;
using AgroShield.Application.DTOs.Auth;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/auth")]
[InternalApiKey]
public class InternalAuthController(ITelegramAuthService tg) : ControllerBase
{
    [HttpPost("register/start")]
    public async Task<IActionResult> Start([FromBody] BotRegisterStartRequest req)
    {
        await tg.BotStartRegistrationAsync(req.Email);
        return Ok(new { message = "Code sent" });
    }

    [HttpPost("register/verify")]
    public async Task<IActionResult> Verify([FromBody] BotRegisterVerifyRequest req)
    {
        var ctx = await tg.BotCompleteRegistrationAsync(req);
        return Ok(new
        {
            user_id   = ctx.UserId,
            email     = ctx.Email,
            full_name = ctx.FullName,
            role      = ctx.Role,
            farm_id   = ctx.FarmId,
        });
    }

    [HttpPost("link-existing")]
    public async Task<IActionResult> Link([FromBody] BotLinkExistingRequest req)
    {
        var ctx = await tg.BotLinkExistingAsync(req);
        return Ok(new
        {
            user_id   = ctx.UserId,
            email     = ctx.Email,
            full_name = ctx.FullName,
            role      = ctx.Role,
            farm_id   = ctx.FarmId,
        });
    }

    [HttpGet("by-chat-id")]
    public async Task<IActionResult> ByChatId([FromQuery(Name = "chat_id")] long chatId)
    {
        var ctx = await tg.GetByChatIdAsync(chatId);
        if (ctx is null)
            return NotFound(new { error = "NotFound", message = "Chat not linked", details = new { } });

        return Ok(new
        {
            user_id   = ctx.UserId,
            email     = ctx.Email,
            full_name = ctx.FullName,
            role      = ctx.Role,
            farm_id   = ctx.FarmId,
        });
    }
}
