using AgroShield.Application.DTOs.Sensors;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgroShield.Infrastructure.Services;

public class SensorService(
    AppDbContext db,
    IRealtimePublisher publisher,
    ILogger<SensorService> logger) : ISensorService
{
    public async Task<SensorReadingDto> SaveReadingAsync(CreateSensorReadingDto dto, CancellationToken ct = default)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.DeviceId == dto.DeviceId, ct)
            ?? throw new KeyNotFoundException($"Farm not found for device '{dto.DeviceId}'");

        var reading = new SensorReading
        {
            FarmId = farm.Id,
            DeviceId = dto.DeviceId,
            Temp = dto.Temp,
            Humidity = dto.Humidity,
            Light = dto.Light,
            Fire = dto.Fire,
            WaterLevel = dto.WaterLevel,
            RecordedAt = dto.RecordedAt ?? DateTime.UtcNow,
        };
        db.SensorReadings.Add(reading);
        await db.SaveChangesAsync(ct);

        var result = ToDto(reading);

        // Create DB records first (sequential, shared DbContext)
        object? firePayload = null;
        if (dto.Fire)
        {
            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                UserId = null,
                Type = AlertType.Fire,
                Title = $"Пожарная тревога: {farm.Name}",
                Message = $"Датчик {dto.DeviceId} зафиксировал признаки возгорания. Temp={dto.Temp}°C.",
                FarmId = farm.Id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);

            firePayload = new { alertId = alert.Id, farmId = farm.Id, farmName = farm.Name, deviceId = dto.DeviceId, temp = dto.Temp, detectedAt = reading.RecordedAt };
            logger.LogWarning("[TELEGRAM MOCK] Would send fire alert for farm {FarmId} ({FarmName})", farm.Id, farm.Name);
        }

        if (dto.WaterLevel < 300)
        {
            db.Recommendations.Add(new Recommendation
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Priority = RecommendationPriority.High,
                Title = "Критически низкий уровень воды",
                Description = $"Датчик {dto.DeviceId} зафиксировал уровень воды {dto.WaterLevel} (норма ≥ 300). Проверьте ирригационную систему.",
                Status = RecommendationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        // Parallel SignalR pushes (data already saved — failures don't matter)
        var pushTasks = new List<Task>
        {
            Safe(() => publisher.PushSensorReadingAsync(farm.Id, result), "sensor-push"),
        };
        if (firePayload is not null)
            pushTasks.Add(Safe(() => publisher.PushFireAlertAsync(farm.Id, firePayload), "fire-push"));

        await Task.WhenAll(pushTasks);

        return result;
    }

    public async Task SaveRfidScanAsync(RfidScanDto dto, CancellationToken ct = default)
    {
        var animal = await db.Animals.FirstOrDefaultAsync(a => a.RfidTag == dto.RfidTag, ct);
        if (animal is null)
        {
            logger.LogWarning("RFID tag {Tag} not registered", dto.RfidTag);
            return;
        }

        var activity = new AnimalActivity
        {
            AnimalId = animal.Id,
            DeviceId = dto.DeviceId,
            DetectedAt = dto.ScannedAt ?? DateTime.UtcNow,
        };
        db.AnimalActivities.Add(activity);
        await db.SaveChangesAsync(ct);

        var payload = new { animalId = animal.Id, rfidTag = dto.RfidTag, species = animal.Species, detectedAt = activity.DetectedAt };
        await Safe(() => publisher.PushRfidScanAsync(dto.FarmId, payload), "rfid-push");
    }

    public async Task<SensorReadingDto?> GetLatestAsync(Guid farmId, CancellationToken ct = default)
    {
        var r = await db.SensorReadings
            .Where(x => x.FarmId == farmId)
            .OrderByDescending(x => x.RecordedAt)
            .FirstOrDefaultAsync(ct);
        return r is null ? null : ToDto(r);
    }

    public async Task<IReadOnlyList<SensorReadingDto>> GetHistoryAsync(
        Guid farmId, string period, CancellationToken ct = default)
    {
        var since = period switch
        {
            "7d"  => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _     => DateTime.UtcNow.AddHours(-24),
        };

        return await db.SensorReadings
            .Where(x => x.FarmId == farmId && x.RecordedAt >= since)
            .OrderByDescending(x => x.RecordedAt)
            .Select(x => ToDto(x))
            .ToListAsync(ct);
    }

    private async Task Safe(Func<Task> action, string label)
    {
        try { await action(); }
        catch (Exception ex) { logger.LogError(ex, "Background task '{Label}' failed", label); }
    }

    private static SensorReadingDto ToDto(SensorReading r) =>
        new(r.Id, r.FarmId, r.DeviceId, r.Temp, r.Humidity, r.Light, r.Fire, r.WaterLevel, r.RecordedAt);
}
