namespace AgroShield.Application.DTOs.ML;

public record NdviResult(
    decimal MeanNdvi,
    decimal ActiveAreaRatio,
    decimal TotalAreaHectares,
    decimal ActiveAreaHectares,
    string? ImageBase64,
    string CapturedDate,
    string Source,
    bool IsMock);
