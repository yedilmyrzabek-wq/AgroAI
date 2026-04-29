namespace AgroShield.Domain.Entities;

public class SubsidyTranche
{
    public Guid Id { get; set; }
    public Guid SubsidyId { get; set; }
    public int Order { get; set; }
    public decimal PercentOfTotal { get; set; }
    public decimal AmountKzt { get; set; }
    public string Status { get; set; } = "pending";
    public string UnlockCondition { get; set; } = "registered";
    public DateTime? ReleasedAt { get; set; }
    public string? ReleaseEvidenceJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public Subsidy Subsidy { get; set; } = null!;
}
