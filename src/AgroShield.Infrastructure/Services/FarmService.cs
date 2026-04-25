using AgroShield.Application.Auth;
using AgroShield.Application.DTOs;
using AgroShield.Application.DTOs.Farms;
using AgroShield.Application.DTOs.Sensors;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Infrastructure.Services;

public class FarmService(AppDbContext db, ICurrentUserAccessor currentUser) : IFarmService
{
    public async Task<PagedResultDto<FarmListItemDto>> GetAllAsync(FarmFilterDto f, CancellationToken ct = default)
    {
        var q = db.Farms.Include(x => x.Owner).AsQueryable();

        if (currentUser.Role == Role.Farmer)
            q = q.Where(x => x.OwnerId == currentUser.UserId);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            q = q.Where(x => x.Name.ToLower().Contains(s)
                           || x.Region.ToLower().Contains(s)
                           || x.District.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(f.Region))   q = q.Where(x => x.Region == f.Region);
        if (!string.IsNullOrWhiteSpace(f.District))  q = q.Where(x => x.District == f.District);
        if (!string.IsNullOrWhiteSpace(f.CropType))  q = q.Where(x => x.CropType == f.CropType);
        if (f.MinRisk.HasValue) q = q.Where(x => x.RiskScore >= f.MinRisk.Value);
        if (f.MaxRisk.HasValue) q = q.Where(x => x.RiskScore <= f.MaxRisk.Value);

        q = (f.SortBy?.ToLower(), f.Order?.ToLower()) switch
        {
            ("risk",  "asc")  => q.OrderBy(x => x.RiskScore),
            ("risk",  _)      => q.OrderByDescending(x => x.RiskScore),
            ("area",  "desc") => q.OrderByDescending(x => x.AreaHectares),
            ("area",  _)      => q.OrderBy(x => x.AreaHectares),
            ("name",  "desc") => q.OrderByDescending(x => x.Name),
            _                 => q.OrderBy(x => x.Name),
        };

        var total = await q.CountAsync(ct);
        var page  = Math.Max(1, f.Page);
        var size  = Math.Clamp(f.PageSize, 1, 100);

        var items = await q
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new FarmListItemDto(
                x.Id, x.Name, x.Region, x.District, x.CropType,
                x.AreaHectares, x.RiskScore, x.Lat, x.Lng,
                x.OwnerId, x.Owner.FullName, x.CreatedAt))
            .ToListAsync(ct);

        return new PagedResultDto<FarmListItemDto>(items, total, page, size);
    }

    public async Task<FarmDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var farm = await db.Farms.Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException($"Farm {id} not found");

        var latest = await db.SensorReadings
            .Where(r => r.FarmId == id)
            .OrderByDescending(r => r.RecordedAt)
            .Select(r => new SensorReadingDto(r.Id, r.FarmId, r.DeviceId, r.Temp, r.Humidity, r.Light, r.Fire, r.WaterLevel, r.RecordedAt))
            .FirstOrDefaultAsync(ct);

        var activeSubsidies = await db.Subsidies
            .CountAsync(s => s.FarmId == id && s.Status == Domain.Enums.SubsidyStatus.Pending || s.Status == Domain.Enums.SubsidyStatus.UnderReview, ct);

        var activeAnomalies = await db.Anomalies
            .CountAsync(a => a.FarmId == id && a.Status == Domain.Enums.AnomalyStatus.Active || a.Status == Domain.Enums.AnomalyStatus.InProgress, ct);

        return new FarmDetailDto(
            farm.Id, farm.Name, farm.Region, farm.District, farm.CropType,
            farm.AreaHectares, farm.RiskScore, farm.Lat, farm.Lng,
            farm.DeviceId, farm.PolygonGeoJson,
            farm.OwnerId, farm.Owner.FullName,
            farm.CreatedAt, farm.UpdatedAt,
            latest, activeSubsidies, activeAnomalies);
    }

    public async Task<FarmDetailDto> CreateAsync(CreateFarmDto dto, CancellationToken ct = default)
    {
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            OwnerId = currentUser.UserId,
            Name = dto.Name,
            Region = dto.Region,
            District = dto.District,
            AreaHectares = dto.AreaHectares,
            Lat = dto.Lat,
            Lng = dto.Lng,
            CropType = dto.CropType,
            DeviceId = dto.DeviceId,
            PolygonGeoJson = dto.PolygonGeoJson ?? "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Farms.Add(farm);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(farm.Id, ct);
    }

    public async Task<FarmDetailDto> UpdateAsync(Guid id, UpdateFarmDto dto, CancellationToken ct = default)
    {
        var farm = await db.Farms.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Farm {id} not found");

        if (dto.Name is not null)         farm.Name = dto.Name;
        if (dto.Region is not null)       farm.Region = dto.Region;
        if (dto.District is not null)     farm.District = dto.District;
        if (dto.AreaHectares.HasValue)    farm.AreaHectares = dto.AreaHectares.Value;
        if (dto.Lat.HasValue)             farm.Lat = dto.Lat.Value;
        if (dto.Lng.HasValue)             farm.Lng = dto.Lng.Value;
        if (dto.CropType is not null)     farm.CropType = dto.CropType;
        if (dto.DeviceId is not null)     farm.DeviceId = dto.DeviceId;
        if (dto.PolygonGeoJson is not null) farm.PolygonGeoJson = dto.PolygonGeoJson;
        farm.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var farm = await db.Farms.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Farm {id} not found");
        db.Farms.Remove(farm);
        await db.SaveChangesAsync(ct);
    }
}
