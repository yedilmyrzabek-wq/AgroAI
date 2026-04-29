using AgroShield.Application.DTOs.SupplyChain;

namespace AgroShield.Application.Services;

public interface ISupplyChainService
{
    Task<SupplyChainLedgerRecordDto> AppendAsync(
        string batchId,
        string eventType,
        object payload,
        Guid? actorId,
        CancellationToken ct = default);

    Task<List<SupplyChainLedgerRecordDto>> GetLedgerAsync(string batchId, CancellationToken ct = default);

    Task<VerifyChainResult> VerifyAsync(string batchId, CancellationToken ct = default);
}
