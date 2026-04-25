using AgroShield.Application.DTOs.Sensors;

namespace AgroShield.Application.Services;

public interface ISensorService
{
    Task<SensorReadingDto> SaveReadingAsync(CreateSensorReadingDto dto, CancellationToken ct = default);
    Task SaveRfidScanAsync(RfidScanDto dto, CancellationToken ct = default);
    Task<SensorReadingDto?> GetLatestAsync(Guid farmId, CancellationToken ct = default);
    Task<IReadOnlyList<SensorReadingDto>> GetHistoryAsync(Guid farmId, string period, CancellationToken ct = default);
}
