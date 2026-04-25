namespace AgroShield.Api.Hubs;

public static class HubGroups
{
    public static string Farm(Guid farmId) => $"farm-{farmId}";
    public const string Inspectors = "inspectors";
    public static string Farmer(Guid userId) => $"farmer-{userId}";
}
