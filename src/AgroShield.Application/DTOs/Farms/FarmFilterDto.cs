namespace AgroShield.Application.DTOs.Farms;

public class FarmFilterDto
{
    public string? Search { get; set; }
    public string? Region { get; set; }
    public string? District { get; set; }
    public string? CropType { get; set; }
    public int? MinRisk { get; set; }
    public int? MaxRisk { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }  // risk | area | name
    public string? Order { get; set; }   // asc | desc
}
