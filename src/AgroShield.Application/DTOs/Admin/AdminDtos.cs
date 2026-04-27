namespace AgroShield.Application.DTOs.Admin;

public record AdminUserDto(
    Guid Id,
    string Email,
    string? FullName,
    string Role,
    string? AssignedRegion,
    bool TelegramLinked,
    bool IsActive,
    DateTime CreatedAt);

public record AdminUserFilter
{
    public string? Role { get; init; }
    public string? Region { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record AssignRegionRequest(string? Region);

public record DemoResetSummary(
    int UsersCreated,
    int FarmsCreated,
    int SensorsCreated,
    int SensorReadingsCreated,
    int DiagnosesCreated,
    int AnomaliesCreated,
    int SubsidiesCreated,
    int BatchesCreated,
    int LivestockCreated,
    int LedgerEntriesCreated,
    int SubscriptionsCreated,
    int KnowledgeChunksCreated,
    long ElapsedMs);
