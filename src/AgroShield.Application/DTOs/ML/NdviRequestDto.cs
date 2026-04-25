namespace AgroShield.Application.DTOs.ML;

public class NdviRequestDto
{
    public List<double[]> Polygon { get; set; } = [];
    public string DateFrom { get; set; } = null!;
    public string DateTo { get; set; } = null!;
}
