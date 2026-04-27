namespace AgroShield.Domain.Entities;

public class SupplyChainTransition
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string FromNodeType { get; set; } = null!;
    public string FromNodeId { get; set; } = null!;
    public string ToNodeType { get; set; } = null!;
    public string ToNodeId { get; set; } = null!;
    public decimal WeightKg { get; set; }
    public DateTime TransferredAt { get; set; }
    public string? Notes { get; set; }

    public SupplyChainBatch Batch { get; set; } = null!;
}
