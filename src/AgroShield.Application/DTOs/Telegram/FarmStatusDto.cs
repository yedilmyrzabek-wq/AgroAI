namespace AgroShield.Application.DTOs.Telegram;

public record FarmStatusDto(
    bool Linked,
    string? FarmName,
    decimal? TemperatureC,
    decimal? HumidityPct,
    int? LightLux,
    decimal? WaterLevelPct,
    double? MinutesAgo);
