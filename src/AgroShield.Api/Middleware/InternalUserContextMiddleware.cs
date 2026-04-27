using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace AgroShield.Api.Middleware;

/// <summary>
/// Allows ai-assistant tools to call public/admin endpoints using
/// X-Internal-Key + X-User-Id headers in lieu of a JWT bearer token.
/// Builds a synthetic ClaimsPrincipal so [Authorize] passes downstream.
/// </summary>
public class InternalUserContextMiddleware(RequestDelegate next, IConfiguration config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var alreadyAuthed = context.User.Identity?.IsAuthenticated == true;
        var expected = config["Security:InternalApiKey"];
        if (!alreadyAuthed &&
            !string.IsNullOrEmpty(expected) &&
            context.Request.Headers.TryGetValue("X-Internal-Key", out var key) &&
            key == expected &&
            context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) &&
            Guid.TryParse(userIdHeader, out var userId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Email, u.Role, u.FullName, u.AssignedRegion })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (user is not null)
            {
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new(JwtRegisteredClaimNames.Email, user.Email),
                    new("role", user.Role.ToString()),
                    new("name", user.FullName ?? user.Email),
                };
                if (!string.IsNullOrWhiteSpace(user.AssignedRegion))
                    claims.Add(new Claim("region", user.AssignedRegion));

                var identity = new ClaimsIdentity(claims, authenticationType: "InternalKey");
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await next(context);
    }
}
