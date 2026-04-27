using AgroShield.Application.DTOs.Admin;

namespace AgroShield.Application.Services;

public interface IDemoSeedService
{
    Task<DemoResetSummary> RunAsync(CancellationToken ct = default);
}
