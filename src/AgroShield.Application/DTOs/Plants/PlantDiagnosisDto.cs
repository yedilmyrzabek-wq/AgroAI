namespace AgroShield.Application.DTOs.Plants;

public record PlantDiagnosisDto(
    Guid Id,
    Guid FarmId,
    Guid UserId,
    string ImageUrl,
    string Disease,
    string DiseaseRu,
    decimal Confidence,
    string Severity,
    bool IsHealthy,
    string Recommendation,
    string ModelVersion,
    DateTime CreatedAt);
