namespace AgroShield.Application.DTOs.Subsidies;

public class CreateSubsidyDto
{
    public Guid FarmId { get; set; }
    public decimal Amount { get; set; }
    public decimal DeclaredArea { get; set; }
    public string Purpose { get; set; } = null!;
}
