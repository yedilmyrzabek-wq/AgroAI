namespace AgroShield.Application.Services;

public interface IVoiceEscalationService
{
    Task RunAsync(Guid anomalyId, CancellationToken ct = default);
}
