namespace AgroShield.Application.DTOs.SupplyChain;

public record FreezeBatchRequest(string Reason);

public record UnfreezeBatchRequest(string Reason);

public record FreezeClusterRequest(Guid AnomalyId, string Reason);

public record FreezeActorDto(Guid Id, string FullName);

public record FreezeBatchResponse(
    Guid BatchId,
    string Status,
    DateTime FrozenAt,
    FreezeActorDto FrozenBy,
    string Reason,
    Guid AuditLogId,
    NotificationStatusDto NotificationSent);

public record UnfreezeBatchResponse(
    Guid BatchId,
    string Status,
    DateTime UnfrozenAt,
    FreezeActorDto UnfrozenBy,
    string Reason,
    Guid AuditLogId);

public record NotificationStatusDto(bool Telegram, bool Email);

public record AuditLogActorDto(Guid Id, string FullName, string Role);

public record AuditLogItemDto(
    Guid Id,
    string Action,
    DateTime PerformedAt,
    AuditLogActorDto PerformedBy,
    string? Reason,
    object? Metadata);

public record AuditLogResponse(Guid BatchId, IReadOnlyList<AuditLogItemDto> Items);

public record AffectedFarmDto(Guid Id, string Name, int FrozenBatches);

public record FreezeClusterResponse(
    Guid AnomalyId,
    int FrozenCount,
    int SkippedCount,
    IReadOnlyList<AffectedFarmDto> AffectedFarms,
    IReadOnlyList<Guid> BatchIds,
    IReadOnlyList<Guid> AuditLogIds,
    int NotificationsSent);
