namespace AgroShield.Application.DTOs.ML;

public record YieldResult(
    decimal PredictedYieldTonnesPerHa,
    decimal TotalYieldTonnes,
    decimal ConfidenceIntervalLower,
    decimal ConfidenceIntervalUpper,
    string ModelVersion,
    string TrainedAt);
