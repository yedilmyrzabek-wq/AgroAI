using AgroShield.Application.DTOs.ML;
using AgroShield.Application.Services;
using AgroShield.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.ExternalServices;

public class MLProxyService(IHttpClientFactory factory, ILogger<MLProxyService> logger) : IMLProxyService
{
    private static readonly JsonSerializerOptions SnakeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task<DiagnosisResult> DiagnosePlantAsync(Stream image, string fileName, Guid? farmId, CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            var client = factory.CreateClient("PlantCv");
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(image), "file", fileName);
            if (farmId.HasValue)
                content.Add(new StringContent(farmId.Value.ToString()), "farm_id");

            var response = await client.PostAsync("/diagnose", content, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<DiagnosisResult>(SnakeOpts, ct))!;
        }, "PlantCv");

    public Task<YieldResult> PredictYieldAsync(YieldFeaturesDto dto, CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            var client = factory.CreateClient("YieldPredictor");
            var response = await client.PostAsJsonAsync("/predict-yield", dto, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<YieldResult>(SnakeOpts, ct))!;
        }, "YieldPredictor");

    public Task<AnomalyCheckResult> CheckAnomalyAsync(SubsidyCheckDto dto, CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            var client = factory.CreateClient("AnomalyDetector");
            var response = await client.PostAsJsonAsync("/check-subsidy", dto, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AnomalyCheckResult>(SnakeOpts, ct))!;
        }, "AnomalyDetector");

    public Task<NdviResult> GetNdviAsync(NdviRequestDto dto, CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            var client = factory.CreateClient("SatelliteNdvi");
            var response = await client.PostAsJsonAsync("/ndvi", dto, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<NdviResult>(SnakeOpts, ct))!;
        }, "SatelliteNdvi");

    public Task<bool> SendTelegramAsync(long chatId, string message, string parseMode = "Markdown", CancellationToken ct = default) =>
        WithRetryAsync(async () =>
        {
            var client = factory.CreateClient("TelegramBot");
            var payload = new { TelegramChatId = chatId, Message = message, ParseMode = parseMode };
            var response = await client.PostAsJsonAsync("/send", payload, SnakeOpts, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
            return result.GetProperty("sent").GetBoolean();
        }, "TelegramBot");

    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, string service)
    {
        int[] delays = [1000, 2000, 4000];
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "[{Service}] Attempt {N}/3 failed", service, i + 1);
                if (i == 2) throw new ExternalServiceException($"{service} is unavailable after 3 attempts");
                await Task.Delay(delays[i]);
            }
        }
        throw new ExternalServiceException($"{service} is unavailable");
    }
}
