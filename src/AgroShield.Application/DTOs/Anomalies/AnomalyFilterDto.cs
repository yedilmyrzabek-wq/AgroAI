namespace AgroShield.Application.DTOs.Anomalies;

public class AnomalyFilterDto
{
    public string? Status { get; set; }
    public Guid? FarmId { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public int? MinRiskScore { get; set; }
    public int Limit { get; set; } = 200;
    public int Page { get; set; } = 1;
}
