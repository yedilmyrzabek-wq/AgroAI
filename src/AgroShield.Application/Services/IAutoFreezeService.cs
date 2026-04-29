namespace AgroShield.Application.Services;

public interface IAutoFreezeService
{
    Task RunAsync(Guid anomalyId, CancellationToken ct = default);
}
