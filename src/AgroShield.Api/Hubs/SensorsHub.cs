using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AgroShield.Api.Hubs;

[Authorize]
public class SensorsHub(AppDbContext db) : Hub
{
    public async Task JoinFarmGroup(Guid farmId)
    {
        var userId = Guid.Parse(Context.User!.FindFirstValue("sub")!);
        var role = GetRole(Context.User!);

        if (role == Role.Farmer)
        {
            var owns = await db.Farms.AnyAsync(f => f.Id == farmId && f.OwnerId == userId);
            if (!owns)
            {
                await Clients.Caller.SendAsync("Error", "Access denied to this farm group");
                return;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Farm(farmId));
        await Clients.Caller.SendAsync("JoinedGroup", HubGroups.Farm(farmId));
    }

    public async Task LeaveFarmGroup(Guid farmId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Farm(farmId));
    }

    private static Role GetRole(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirstValue(ClaimTypes.Role)
                     ?? user.FindFirstValue("role");
        return Enum.TryParse<Role>(roleClaim, ignoreCase: true, out var r) ? r : Role.Farmer;
    }
}
