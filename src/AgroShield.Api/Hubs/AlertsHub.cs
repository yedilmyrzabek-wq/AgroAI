using AgroShield.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace AgroShield.Api.Hubs;

[Authorize]
public class AlertsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        var userId = Guid.Parse(user.FindFirstValue("sub")!);
        var role = GetRole(user);

        if (role is Role.Inspector or Role.Admin)
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Inspectors);
        else
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Farmer(userId));

        await base.OnConnectedAsync();
    }

    private static Role GetRole(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirstValue(ClaimTypes.Role)
                     ?? user.FindFirstValue("role");
        return Enum.TryParse<Role>(roleClaim, ignoreCase: true, out var r) ? r : Role.Farmer;
    }
}
