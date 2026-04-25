using AgroShield.Application.DTOs.ML;

namespace AgroShield.Application.Services;

public interface IMLProxyService
{
    Task<DiagnosisResult> DiagnosePlantAsync(Stream image, string fileName, Guid? farmId, CancellationToken ct = default);
    Task<YieldResult> PredictYieldAsync(YieldFeaturesDto dto, CancellationToken ct = default);
    Task<AnomalyCheckResult> CheckAnomalyAsync(SubsidyCheckDto dto, CancellationToken ct = default);
    Task<NdviResult> GetNdviAsync(NdviRequestDto dto, CancellationToken ct = default);
    Task<bool> SendTelegramAsync(long chatId, string message, string parseMode = "Markdown", CancellationToken ct = default);
}
