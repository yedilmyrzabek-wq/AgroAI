using AgroShield.Application.DTOs.Climate;

namespace AgroShield.Application.Services;

public interface IClimateRiskService
{
    Task<ClimateRiskDto> GetForFarmAsync(Guid farmId, CancellationToken ct = default);
}
