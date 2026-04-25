using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class WeatherAlertJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<WeatherAlertJob> logger)
{
    public async Task ExecuteAsync()
    {
        var apiKey = config["OpenWeather:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogInformation("WeatherAlertJob: OpenWeather API key not configured, skipping");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRealtimePublisher>();

        var farms = await db.Farms.Take(50).ToListAsync();
        logger.LogInformation("WeatherAlertJob: checking weather for {Count} farms", farms.Count);

        foreach (var farm in farms)
        {
            try
            {
                var forecast = await FetchForecastAsync(apiKey, farm.Lat, farm.Lng);
                if (forecast is null) continue;

                if (forecast.HasFrost || forecast.HasHail)
                {
                    var alert = new Alert
                    {
                        Id        = Guid.NewGuid(),
                        Type      = AlertType.Weather,
                        Title     = $"Погодное предупреждение: {farm.Name}",
                        Message   = forecast.HasFrost
                            ? "Прогнозируются заморозки в ближайшие 24 часа"
                            : "Прогнозируется град в ближайшие 24 часа",
                        FarmId    = farm.Id,
                        IsRead    = false,
                        CreatedAt = DateTime.UtcNow,
                    };
                    db.Alerts.Add(alert);
                    await db.SaveChangesAsync();

                    await publisher.PushFireAlertAsync(farm.Id, new
                    {
                        type    = "weather_alert",
                        farmId  = farm.Id,
                        message = alert.Message,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WeatherAlertJob failed for farm {FarmId}", farm.Id);
            }
        }
    }

    private static async Task<WeatherForecast?> FetchForecastAsync(string apiKey, double lat, double lng)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var url = $"https://api.openweathermap.org/data/2.5/forecast?lat={lat}&lon={lng}&appid={apiKey}&units=metric&cnt=8";
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("list");

        var hasFrost = false;
        var hasHail  = false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.TryGetProperty("main", out var main))
            {
                var tempMin = main.GetProperty("temp_min").GetDouble();
                if (tempMin < 0) hasFrost = true;
            }
            if (item.TryGetProperty("weather", out var weather))
            {
                foreach (var w in weather.EnumerateArray())
                {
                    var id = w.GetProperty("id").GetInt32();
                    if (id is >= 900 and <= 902) hasHail = true;
                }
            }
        }
        return new WeatherForecast(hasFrost, hasHail);
    }

    private record WeatherForecast(bool HasFrost, bool HasHail);
}
