namespace AgroShield.Application.DTOs.ML;

public class YieldFeaturesDto
{
    public decimal AvgTemp { get; set; }
    public decimal AvgHumidity { get; set; }
    public decimal AvgLight { get; set; }
    public decimal NdviMean { get; set; }
    public decimal AreaHectares { get; set; }
    public string CropType { get; set; } = null!;
}
