using AgroShield.Application.DTOs.SupplyChain;

namespace AgroShield.Application.Services;

public interface IBatchFreezeService
{
    Task<FreezeBatchResponse> FreezeAsync(Guid batchId, string reason, CancellationToken ct = default);
    Task<UnfreezeBatchResponse> UnfreezeAsync(Guid batchId, string reason, CancellationToken ct = default);
    Task<AuditLogResponse> GetAuditLogAsync(Guid batchId, CancellationToken ct = default);
    Task<FreezeClusterResponse> FreezeClusterAsync(Guid anomalyId, string reason, CancellationToken ct = default);
}

public interface INotificationDispatcher
{
    Task SendBatchFrozenAsync(Guid batchId);
}
