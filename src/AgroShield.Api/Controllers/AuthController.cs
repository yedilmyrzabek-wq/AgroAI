using AgroShield.Application.Auth;
using AgroShield.Application.DTOs.Auth;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register/start")]
    public async Task<IActionResult> StartRegistration([FromBody] StartRegistrationRequest req)
    {
        await auth.StartRegistrationAsync(req.Email);
        return Ok(new { message = "Verification code sent" });
    }

    [HttpPost("register/verify")]
    public async Task<IActionResult> VerifyRegistration([FromBody] VerifyRegistrationRequest req) =>
        Ok(await auth.CompleteRegistrationAsync(req));

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req) =>
        Ok(await auth.LoginAsync(req));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req) =>
        Ok(await auth.RefreshAsync(req.RefreshToken));

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        await auth.LogoutAsync(req.RefreshToken);
        return NoContent();
    }

    [HttpPost("password/reset/start")]
    public async Task<IActionResult> StartPasswordReset([FromBody] StartPasswordResetRequest req)
    {
        await auth.StartPasswordResetAsync(req.Email);
        return Ok(new { message = "If account exists, code was sent" });
    }

    [HttpPost("password/reset/confirm")]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ConfirmPasswordResetRequest req)
    {
        await auth.ConfirmPasswordResetAsync(req);
        return Ok(new { message = "Password updated" });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me([FromServices] ICurrentUserAccessor user) =>
        Ok(await auth.GetMeAsync(user.UserId));

    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> PatchMe(
        [FromBody] PatchMeRequest req,
        [FromServices] ICurrentUserAccessor user,
        [FromServices] AgroShield.Infrastructure.Persistence.AppDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Users.FindAsync([user.UserId], ct);
        if (entity is null) return NotFound();

        if (req.FullName is not null) entity.FullName = req.FullName;
        if (req.PhoneNumber is not null) entity.PhoneNumber = req.PhoneNumber;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(await auth.GetMeAsync(user.UserId));
    }

    public record PatchMeRequest(string? FullName, string? PhoneNumber);
}
