using AgroShield.Application.Auth;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(ICurrentUserAccessor currentUser, IProfileService profileService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await profileService.GetOrCreateAsync(
            currentUser.UserId, currentUser.Email, currentUser.Role, ct);

        return Ok(new
        {
            user_id = profile.UserId,
            email = currentUser.Email,
            role = profile.Role.ToString(),
            full_name = profile.FullName,
            phone_number = profile.PhoneNumber,
            telegram_chat_id = profile.TelegramChatId,
            farm_id = profile.FarmId,
        });
    }

    [HttpPatch("me")]
    public async Task<IActionResult> PatchMe([FromBody] PatchMeRequest request, CancellationToken ct)
    {
        var profile = await profileService.UpdateAsync(
            currentUser.UserId, request.FullName, request.PhoneNumber, ct);

        return Ok(new
        {
            user_id = profile.UserId,
            role = profile.Role.ToString(),
            full_name = profile.FullName,
            phone_number = profile.PhoneNumber,
            telegram_chat_id = profile.TelegramChatId,
            farm_id = profile.FarmId,
        });
    }

    public record PatchMeRequest(string? FullName, string? PhoneNumber);
}
