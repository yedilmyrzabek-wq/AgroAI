using AgroShield.Application.DTOs.Sensors;

namespace AgroShield.Application.DTOs.Farms;

public record FarmDetailDto(
    Guid Id,
    string Name,
    string Region,
    string District,
    string CropType,
    decimal AreaHectares,
    int RiskScore,
    double Lat,
    double Lng,
    string? DeviceId,
    string PolygonGeoJson,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    SensorReadingDto? LatestReading,
    int ActiveSubsidyCount,
    int ActiveAnomalyCount,
    decimal? NdviMean,
    decimal? ActiveAreaFromNdvi,
    DateTime? NdviUpdatedAt,
    // v3 extensions
    object? NdviHistory = null,
    object? LivestockSummary = null,
    DateTime? FertilizerLastRecommendationAt = null,
    int ActiveBatchesCount = 0);
