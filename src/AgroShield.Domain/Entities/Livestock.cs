namespace AgroShield.Domain.Entities;

public class Livestock
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string LivestockType { get; set; } = null!;
    public int DeclaredCount { get; set; }
    public int? LastDetectedCount { get; set; }
    public DateTime? LastDetectedAt { get; set; }
    public string? LastImageUrl { get; set; }
    public bool AnomalyDetected { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<LivestockLedger> Ledger { get; set; } = [];
}
