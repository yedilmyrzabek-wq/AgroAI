using AgroShield.Domain.Enums;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;

namespace AgroShield.Application.Auth;

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private ClaimsPrincipal User =>
        httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context");

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.Parse(
        User.FindFirstValue("sub") ?? throw new InvalidOperationException("Missing 'sub' claim"));

    public string Email => User.FindFirstValue("email") ?? string.Empty;

    public Role Role
    {
        get
        {
            // Check root-level role claim (Supabase puts "authenticated" here by default)
            var roleClaim = User.FindFirstValue("role");
            if (!string.IsNullOrEmpty(roleClaim) && roleClaim != "authenticated"
                && Enum.TryParse<Role>(roleClaim, ignoreCase: true, out var fromRoot))
                return fromRoot;

            // Check app_metadata.role (set via Admin API)
            var appMeta = User.FindFirstValue("app_metadata");
            if (!string.IsNullOrEmpty(appMeta))
            {
                try
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(appMeta);
                    if (doc.TryGetProperty("role", out var roleEl))
                    {
                        var s = roleEl.GetString();
                        if (s != null && Enum.TryParse<Role>(s, ignoreCase: true, out var fromMeta))
                            return fromMeta;
                    }
                }
                catch { /* malformed JSON — ignore */ }
            }

            return Role.Farmer;
        }
    }
}
