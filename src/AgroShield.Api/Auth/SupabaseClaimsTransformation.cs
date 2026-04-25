using AgroShield.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Json;

namespace AgroShield.Api.Auth;

public class SupabaseClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        string? role = null;

        // Priority 1: app_metadata.role
        var appMeta = principal.FindFirstValue("app_metadata");
        if (!string.IsNullOrEmpty(appMeta))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(appMeta);
                if (doc.TryGetProperty("role", out var el))
                    role = el.GetString();
            }
            catch { }
        }

        // Priority 2: root role claim (skip Supabase's built-in "authenticated")
        if (string.IsNullOrEmpty(role))
        {
            var rootRole = principal.FindFirstValue("role");
            if (!string.IsNullOrEmpty(rootRole) && rootRole != "authenticated")
                role = rootRole;
        }

        if (!string.IsNullOrEmpty(role)
            && Enum.TryParse<Role>(role, ignoreCase: true, out var parsed)
            && !principal.IsInRole(parsed.ToString()))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, parsed.ToString()));
        }

        return Task.FromResult(principal);
    }
}
