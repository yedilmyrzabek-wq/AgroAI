using AgroShield.Domain.Enums;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

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
            var claim = User.FindFirstValue("role");
            if (!string.IsNullOrEmpty(claim) && Enum.TryParse<Role>(claim, ignoreCase: true, out var role))
                return role;
            return Role.Farmer;
        }
    }
}
