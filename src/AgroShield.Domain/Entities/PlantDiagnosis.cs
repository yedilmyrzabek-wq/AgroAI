namespace AgroShield.Domain.Entities;

public class PlantDiagnosis
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid UserId { get; set; }
    public string ImageUrl { get; set; } = null!;
    public string Disease { get; set; } = null!;
    public string DiseaseRu { get; set; } = null!;
    public decimal Confidence { get; set; }
    public string Severity { get; set; } = null!;
    public bool IsHealthy { get; set; }
    public string Recommendation { get; set; } = null!;
    public string ModelVersion { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
