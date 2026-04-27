namespace AgroShield.Domain.Entities;

public class SupplyChainBatch
{
    public Guid Id { get; set; }
    public string BatchCode { get; set; } = null!;
    public Guid FarmId { get; set; }
    public string CropType { get; set; } = null!;
    public decimal InitialWeightKg { get; set; }
    public decimal CurrentWeightKg { get; set; }
    public DateOnly? HarvestDate { get; set; }
    public string CurrentHolderType { get; set; } = "farm";
    public string? CurrentHolderId { get; set; }
    public string Status { get; set; } = "active";
    public bool AnomalyDetected { get; set; }
    public DateTime CreatedAt { get; set; }

    public DateTime? FrozenAt { get; set; }
    public Guid? FrozenBy { get; set; }
    public string? FreezeReason { get; set; }
    public DateTime? UnfrozenAt { get; set; }
    public Guid? UnfrozenBy { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<SupplyChainTransition> Transitions { get; set; } = [];
}
