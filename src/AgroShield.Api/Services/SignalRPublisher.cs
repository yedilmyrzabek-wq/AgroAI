using AgroShield.Api.Hubs;
using AgroShield.Application.DTOs.Sensors;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace AgroShield.Api.Services;

public class SignalRPublisher(
    IHubContext<SensorsHub> sensorsHub,
    IHubContext<AlertsHub> alertsHub) : IRealtimePublisher
{
    public Task PushSensorReadingAsync(Guid farmId, SensorReadingDto reading) =>
        sensorsHub.Clients.Group(HubGroups.Farm(farmId)).SendAsync("SensorUpdated", reading);

    public Task PushFireAlertAsync(Guid farmId, object payload) =>
        Task.WhenAll(
            sensorsHub.Clients.Group(HubGroups.Farm(farmId)).SendAsync("FireAlert", payload),
            alertsHub.Clients.Group(HubGroups.Inspectors).SendAsync("FireAlert", payload));

    public Task PushRfidScanAsync(Guid farmId, object payload) =>
        sensorsHub.Clients.Group(HubGroups.Farm(farmId)).SendAsync("RfidScanned", payload);

    public Task PushBatchFrozenAsync(Guid farmId, object payload) =>
        Task.WhenAll(
            alertsHub.Clients.Group(HubGroups.Farm(farmId)).SendAsync("BatchFrozen", payload),
            alertsHub.Clients.Group(HubGroups.Inspectors).SendAsync("BatchFrozen", payload));
}
