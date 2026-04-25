namespace AgroShield.Application.DTOs.Farms;

public record FarmListItemDto(
    Guid Id,
    string Name,
    string Region,
    string District,
    string CropType,
    decimal AreaHectares,
    int RiskScore,
    double Lat,
    double Lng,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt);
