namespace AgroShield.Application.DTOs.Subsidies;

public record SubsidyTrancheDto(
    Guid Id,
    Guid SubsidyId,
    int Order,
    decimal PercentOfTotal,
    decimal AmountKzt,
    string Status,
    string UnlockCondition,
    DateTime? ReleasedAt,
    string? ReleaseEvidenceJson);

public record SubsidyDetailsDto(
    Guid Id,
    Guid FarmId,
    string FarmName,
    Guid? FarmerId,
    decimal AmountKzt,
    string CropType,
    string Status,           // workflow status: approved|in_progress|completed|frozen
    DateTime CreatedAt,
    DateTime? CompletedAt,
    List<SubsidyTrancheDto> Tranches);

public class CreateSubsidyTrancheDto
{
    public Guid FarmId { get; set; }
    public Guid? FarmerId { get; set; }
    public decimal AmountKzt { get; set; }
    public string CropType { get; set; } = "wheat";
}

public class RecordFertilizerDto
{
    public DateTime AppliedAt { get; set; }
    public decimal? ExpectedGrowthPct { get; set; }
}

public class RecordHarvestDto
{
    public decimal HarvestKg { get; set; }
    public DateTime? HarvestedAt { get; set; }
    public string? Notes { get; set; }
}
