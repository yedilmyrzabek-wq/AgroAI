namespace AgroShield.Application.DTOs.Anomalies;

public record AnomalyDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid FarmId,
    string? FarmName,
    int RiskScore,
    string[] Reasons,
    string Status,
    DateTime DetectedAt,
    DateTime? ResolvedAt,
    string? ResolutionNotes);
