namespace AgroShield.Application.DTOs.Sensors;

public record SensorReadingDto(
    long Id,
    Guid FarmId,
    string DeviceId,
    decimal Temp,
    decimal Humidity,
    int Light,
    bool Fire,
    int WaterLevel,
    DateTime RecordedAt);
