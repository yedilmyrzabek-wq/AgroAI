namespace AgroShield.Application.DTOs.Anomalies;

public class UpdateAnomalyStatusDto
{
    public string NewStatus { get; set; } = null!;
    public string? Notes { get; set; }
}
