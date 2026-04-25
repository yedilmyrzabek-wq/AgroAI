namespace AgroShield.Application.DTOs.Farms;

public record UpdateFarmDto(
    string? Name,
    string? Region,
    string? District,
    decimal? AreaHectares,
    double? Lat,
    double? Lng,
    string? CropType,
    string? DeviceId,
    string? PolygonGeoJson);
