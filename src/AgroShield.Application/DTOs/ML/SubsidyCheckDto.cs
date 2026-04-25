namespace AgroShield.Application.DTOs.ML;

public class SubsidyCheckDto
{
    public decimal DeclaredArea { get; set; }
    public decimal Amount { get; set; }
    public decimal ActiveAreaNdvi { get; set; }
    public decimal SensorActiveHoursPerDay { get; set; }
    public string CropType { get; set; } = null!;
    public decimal? PrevYearDeclared { get; set; }
}
