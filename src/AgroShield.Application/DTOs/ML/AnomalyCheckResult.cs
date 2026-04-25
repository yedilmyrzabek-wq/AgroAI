namespace AgroShield.Application.DTOs.ML;

public record AnomalyCheckResult(
    bool IsSuspicious,
    int RiskScore,
    List<string> Reasons,
    decimal AnomalyScoreRaw,
    string ModelVersion);
