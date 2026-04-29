namespace AgroShield.Domain.Entities;

public class SupplyChainLedgerEntry
{
    public Guid Id { get; set; }
    public string BatchId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string PayloadJson { get; set; } = "{}";
    public Guid? ActorId { get; set; }
    public string PrevHash { get; set; } = null!;
    public string EntryHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
