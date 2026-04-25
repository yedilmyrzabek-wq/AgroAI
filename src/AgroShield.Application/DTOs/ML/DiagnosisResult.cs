namespace AgroShield.Application.DTOs.ML;

public record AlternativeDiagnosis(string Disease, string DiseaseRu, decimal Confidence);

public record DiagnosisResult(
    string Disease,
    string DiseaseRu,
    decimal Confidence,
    string Recommendation,
    string Severity,
    bool IsHealthy,
    bool IsUncertain,
    List<AlternativeDiagnosis> Alternatives,
    string ModelVersion);
