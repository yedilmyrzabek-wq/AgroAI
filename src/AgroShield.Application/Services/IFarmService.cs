using AgroShield.Application.DTOs;
using AgroShield.Application.DTOs.Farms;

namespace AgroShield.Application.Services;

public interface IFarmService
{
    Task<PagedResultDto<FarmListItemDto>> GetAllAsync(FarmFilterDto filter, CancellationToken ct = default);
    Task<FarmDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FarmDetailDto> CreateAsync(CreateFarmDto dto, CancellationToken ct = default);
    Task<FarmDetailDto> UpdateAsync(Guid id, UpdateFarmDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
