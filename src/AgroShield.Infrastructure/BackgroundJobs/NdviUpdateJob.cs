using AgroShield.Application.DTOs.ML;
using AgroShield.Application.Services;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgroShield.Infrastructure.BackgroundJobs;

public class NdviUpdateJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NdviUpdateJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ml = scope.ServiceProvider.GetRequiredService<IMLProxyService>();

        var farms = await db.Farms
            .Where(f => f.RiskScore >= 50)
            .ToListAsync();

        logger.LogInformation("NdviUpdateJob: updating NDVI for {Count} high-risk farms", farms.Count);

        var now = DateTime.UtcNow;
        foreach (var farm in farms)
        {
            try
            {
                var polygon = ParsePolygon(farm.PolygonGeoJson, farm.Lat, farm.Lng);
                var ndvi = await ml.GetNdviAsync(new NdviRequestDto
                {
                    Polygon  = polygon,
                    DateFrom = now.AddDays(-30).ToString("yyyy-MM-dd"),
                    DateTo   = now.ToString("yyyy-MM-dd"),
                });

                farm.NdviMean = ndvi.MeanNdvi;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NdviUpdateJob failed for farm {FarmId}", farm.Id);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("NdviUpdateJob: completed");
    }

    private static List<double[]> ParsePolygon(string geoJson, double lat, double lng)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(geoJson);
            if (doc.TryGetProperty("coordinates", out var coords))
            {
                var points = new List<double[]>();
                foreach (var pt in coords[0].EnumerateArray())
                {
                    var arr = pt.EnumerateArray().ToArray();
                    points.Add([arr[1].GetDouble(), arr[0].GetDouble()]);
                }
                return points;
            }
        }
        catch { }
        const double d = 0.01;
        return [[lat-d,lng-d],[lat-d,lng+d],[lat+d,lng+d],[lat+d,lng-d],[lat-d,lng-d]];
    }
}
