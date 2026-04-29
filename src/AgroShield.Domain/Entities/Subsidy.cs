using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Subsidy
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public decimal Amount { get; set; }
    public decimal DeclaredArea { get; set; }
    public decimal? ActiveAreaFromNdvi { get; set; }
    public decimal? NdviMeanScore { get; set; }
    public string Purpose { get; set; } = null!;
    public SubsidyStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CheckedAt { get; set; }

    // Tranche workflow (TZ v6 §3.5)
    public Guid? FarmerId { get; set; }
    public string? CropType { get; set; }
    public string WorkflowStatus { get; set; } = "approved"; // approved|in_progress|completed|frozen
    public DateTime? CompletedAt { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<SubsidyTranche> Tranches { get; set; } = [];
}
