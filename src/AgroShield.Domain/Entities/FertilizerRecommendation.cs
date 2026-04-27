namespace AgroShield.Domain.Entities;

public class FertilizerRecommendation
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public decimal? NKgPerHa { get; set; }
    public decimal? PKgPerHa { get; set; }
    public decimal? KKgPerHa { get; set; }
    public decimal? TotalKg { get; set; }
    public decimal? EstimatedCostKzt { get; set; }
    public decimal? ExpectedYieldIncreasePct { get; set; }
    public string? ApplicationWindows { get; set; }
    public string? ExplanationRu { get; set; }
    public string? ModelVersion { get; set; }
    public DateTime CreatedAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
