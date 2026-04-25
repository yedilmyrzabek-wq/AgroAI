using AgroShield.Application.DTOs.Sensors;

namespace AgroShield.Application.Services;

public interface IRealtimePublisher
{
    Task PushSensorReadingAsync(Guid farmId, SensorReadingDto reading);
    Task PushFireAlertAsync(Guid farmId, object payload);
    Task PushRfidScanAsync(Guid farmId, object payload);
}
