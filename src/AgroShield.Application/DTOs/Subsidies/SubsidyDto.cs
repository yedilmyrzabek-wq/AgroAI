namespace AgroShield.Application.DTOs.Subsidies;

public record SubsidyDto(
    Guid Id,
    Guid FarmId,
    string FarmName,
    decimal Amount,
    decimal DeclaredArea,
    decimal? ActiveAreaFromNdvi,
    decimal? NdviMeanScore,
    string Purpose,
    string Status,
    DateTime SubmittedAt,
    DateTime? CheckedAt);
