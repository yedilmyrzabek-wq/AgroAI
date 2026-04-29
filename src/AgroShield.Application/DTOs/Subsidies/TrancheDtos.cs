using System.Text.Json.Serialization;

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
    [JsonPropertyName("farm_id")]
    public Guid FarmId { get; set; }

    [JsonPropertyName("farmer_id")]
    public Guid? FarmerId { get; set; }

    [JsonPropertyName("amount_kzt")]
    public decimal AmountKzt { get; set; }

    [JsonPropertyName("crop_type")]
    public string CropType { get; set; } = "wheat";
}

public class RecordFertilizerDto
{
    [JsonPropertyName("applied_at")]
    public DateTime AppliedAt { get; set; }

    [JsonPropertyName("expected_growth_pct")]
    public decimal? ExpectedGrowthPct { get; set; }
}

public class RecordHarvestDto
{
    [JsonPropertyName("harvest_kg")]
    public decimal HarvestKg { get; set; }

    [JsonPropertyName("harvested_at")]
    public DateTime? HarvestedAt { get; set; }

    public string? Notes { get; set; }
}
