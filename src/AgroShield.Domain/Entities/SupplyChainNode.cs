namespace AgroShield.Domain.Entities;

public class SupplyChainNode
{
    public Guid Id { get; set; }
    public Guid FromEntityId { get; set; }
    public string FromEntityName { get; set; } = null!;
    public Guid ToEntityId { get; set; }
    public string ToEntityName { get; set; } = null!;
    public string Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = null!;
    public string TransactionHash { get; set; } = null!;
    public string PreviousHash { get; set; } = null!;
    public DateTime TransactionDate { get; set; }
    public bool IsSuspicious { get; set; }
    public DateTime CreatedAt { get; set; }
}
