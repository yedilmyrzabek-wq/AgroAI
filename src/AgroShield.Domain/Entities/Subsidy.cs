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

    public Farm Farm { get; set; } = null!;
}
