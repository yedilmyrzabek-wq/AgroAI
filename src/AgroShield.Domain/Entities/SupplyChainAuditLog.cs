namespace AgroShield.Domain.Entities;

public class SupplyChainAuditLog
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string Action { get; set; } = null!;
    public Guid PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }

    public SupplyChainBatch Batch { get; set; } = null!;
    public User PerformedByUser { get; set; } = null!;
}
