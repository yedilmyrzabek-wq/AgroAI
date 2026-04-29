namespace AgroShield.Application.DTOs.SupplyChain;

public record SupplyChainLedgerRecordDto(
    Guid Id,
    string BatchId,
    string EventType,
    string PayloadJson,
    Guid? ActorId,
    string PrevHash,
    string EntryHash,
    DateTime CreatedAt);

public record VerifyChainResult(
    bool IsValid,
    int Length,
    DateTime? BrokenAtTimestamp,
    int? BrokenAtIndex);
