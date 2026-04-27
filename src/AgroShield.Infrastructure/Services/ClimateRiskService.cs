using AgroShield.Application.DTOs.Climate;
using AgroShield.Application.Services;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class ClimateRiskService(
    AppDbContext db,
    IHttpClientFactory factory,
    IMemoryCache cache,
    ILogger<ClimateRiskService> logger) : IClimateRiskService
{
    private const string ModelVersion = "climate_north_kz_v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    public async Task<ClimateRiskDto> GetForFarmAsync(Guid farmId, CancellationToken ct = default)
    {
        var key = $"climate-risk:{farmId}";
        if (cache.TryGetValue(key, out ClimateRiskDto? cached) && cached is not null)
            return cached;

        var farm = await db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => new { f.Id, f.Lat, f.Lng })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Farm not found");

        var (frostRisk, droughtRisk, windErosion, ndviVolatility) = await ComputeFactorsAsync(farm.Lat, farm.Lng, farmId, ct);

        var factors = new List<ClimateRiskFactorDto>
        {
            new("frost_risk_may",    0.40, frostRisk,      ExplainFrost(frostRisk)),
            new("drought_risk_july", 0.30, droughtRisk,    ExplainDrought(droughtRisk)),
            new("wind_erosion",      0.20, windErosion,    ExplainWind(windErosion)),
            new("ndvi_volatility",   0.10, ndviVolatility, ExplainNdvi(ndviVolatility)),
        };

        var rawScore = factors.Sum(f => f.Weight * f.Value * 100);
        var score = (int)Math.Round(Math.Clamp(rawScore, 0, 100));
        var level = score switch
        {
            < 30 => "low",
            < 60 => "medium",
            < 85 => "high",
            _    => "critical",
        };

        var recommendation = BuildRecommendation(frostRisk, droughtRisk, windErosion);

        var dto = new ClimateRiskDto(farmId, score, level, factors, recommendation, ModelVersion, DateTime.UtcNow);
        cache.Set(key, dto, CacheTtl);
        return dto;
    }

    private async Task<(double frost, double drought, double wind, double ndviVol)> ComputeFactorsAsync(
        double lat, double lng, Guid farmId, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                      $"&longitude={lng.ToString(CultureInfo.InvariantCulture)}" +
                      "&forecast_days=14&past_days=30" +
                      "&daily=temperature_2m_min,temperature_2m_max,precipitation_sum,wind_speed_10m_max" +
                      "&timezone=auto";

            var json = await client.GetFromJsonAsync<JsonElement>(url, ct);
            var daily = json.GetProperty("daily");

            var minTemps = ReadDoubles(daily, "temperature_2m_min");
            var precip   = ReadDoubles(daily, "precipitation_sum");
            var wind     = ReadDoubles(daily, "wind_speed_10m_max");

            var frost = ComputeFrostRisk(minTemps);
            var drought = ComputeDroughtRisk(precip);
            var windErosion = ComputeWindErosion(wind);
            var ndviVol = await ComputeNdviVolatilityAsync(farmId, ct);

            return (frost, drought, windErosion, ndviVol);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open-Meteo unreachable, returning fallback factors for farm {FarmId}", farmId);
            return (0.5, 0.4, 0.3, 0.5);
        }
    }

    private static double[] ReadDoubles(JsonElement daily, string key)
    {
        if (!daily.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<double>();
        return arr.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0d)
            .ToArray();
    }

    private static double ComputeFrostRisk(double[] minTemps)
    {
        if (minTemps.Length == 0) return 0.5;
        var below = minTemps.Count(t => t < 0);
        return Math.Clamp(below / 10.0, 0, 1);
    }

    private static double ComputeDroughtRisk(double[] precip)
    {
        if (precip.Length == 0) return 0.5;
        var totalNext14 = precip.Skip(Math.Max(0, precip.Length - 14)).Sum();
        // 50mm = норма для июля: меньше — суше
        return Math.Clamp((50 - totalNext14) / 50.0, 0, 1);
    }

    private static double ComputeWindErosion(double[] wind)
    {
        if (wind.Length == 0) return 0.3;
        var max = wind.Max();
        return Math.Clamp((max - 5) / 10.0, 0, 1); // 5 m/s baseline, 15 m/s = max
    }

    private async Task<double> ComputeNdviVolatilityAsync(Guid farmId, CancellationToken ct)
    {
        var farm = await db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => f.NdviHistoryJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(farm)) return 0.5;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(farm);
            if (doc.ValueKind != JsonValueKind.Array) return 0.5;
            var values = doc.EnumerateArray()
                .Select(e => e.TryGetProperty("ndvi", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : double.NaN)
                .Where(d => !double.IsNaN(d))
                .ToArray();
            if (values.Length < 2) return 0.5;
            var mean = values.Average();
            var variance = values.Average(v => (v - mean) * (v - mean));
            var stdDev = Math.Sqrt(variance);
            return Math.Clamp(stdDev / 0.2, 0, 1);
        }
        catch { return 0.5; }
    }

    private static string ExplainFrost(double v)
    {
        if (v >= 0.7) return $"Высокий риск заморозков в ближайшие 14 дней (отрицательные минимумы)";
        if (v >= 0.4) return "Умеренный риск ночных заморозков в ближайшие две недели";
        return "Заморозков в прогнозе не ожидается";
    }

    private static string ExplainDrought(double v)
    {
        if (v >= 0.7) return "Прогноз осадков ниже нормы — высокий риск засухи";
        if (v >= 0.4) return "Осадки ниже нормы — умеренный риск засухи";
        return "Осадки в норме";
    }

    private static string ExplainWind(double v)
    {
        if (v >= 0.6) return "Прогнозируется сильный ветер до 11+ м/с — риск эрозии";
        if (v >= 0.3) return "Ветер умеренный, частичный риск эрозии";
        return "Ветер в норме";
    }

    private static string ExplainNdvi(double v)
    {
        if (v >= 0.6) return "Высокая нестабильность NDVI за сезон";
        if (v >= 0.3) return "Умеренная вариативность NDVI";
        return "NDVI стабилен";
    }

    private static string BuildRecommendation(double frost, double drought, double wind)
    {
        var parts = new List<string>();
        if (frost > 0.7) parts.Add("Рекомендуем застраховать 70% посевов от заморозков");
        if (drought > 0.6) parts.Add("Запланируйте дополнительное орошение к концу июля");
        if (wind > 0.6) parts.Add("Установите ветрозащитные экраны на наветренных границах поля");
        if (parts.Count == 0) parts.Add("Проводите регулярный мониторинг поля и сенсоров");
        return string.Join(". ", parts) + ".";
    }
}
