namespace AgroShield.Application.DTOs.Climate;

public record ClimateRiskFactorDto(string Name, double Weight, double Value, string ExplanationRu);

public record ClimateRiskDto(
    Guid FarmId,
    int Score,
    string Level,
    IReadOnlyList<ClimateRiskFactorDto> Factors,
    string RecommendationRu,
    string ModelVersion,
    DateTime CalculatedAt);
