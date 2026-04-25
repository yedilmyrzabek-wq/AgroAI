namespace AgroShield.Application.DTOs.Plants;

public class PlantDiagnosisFilterDto
{
    public Guid? FarmId { get; set; }
    public string? Severity { get; set; }
    public int Limit { get; set; } = 20;
}
