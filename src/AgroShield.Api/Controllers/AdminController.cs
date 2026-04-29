using AgroShield.Application.DTOs;
using AgroShield.Application.DTOs.Admin;
using AgroShield.Application.Services;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, IDemoSeedService seed, IHttpClientFactory httpFactory, ILogger<AdminController> logger) : ControllerBase
{
    private static readonly string[] MlServiceNames =
    [
        "PlantCv", "YieldPredictor", "AnomalyDetector", "AiAssistant",
        "SatelliteNdvi", "TelegramBot", "LivestockMonitor",
        "FertilizerAdvisor", "SupplyChainTracker"
    ];

    [HttpGet("ml-smoke-9")]
    public async Task<IActionResult> MlSmoke9(CancellationToken ct)
    {
        var results = new Dictionary<string, object>();
        var tasks = MlServiceNames.Select(async name =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var client = httpFactory.CreateClient(name);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var resp = await client.GetAsync("/health", cts.Token);
                sw.Stop();
                return (name, status: (int)resp.StatusCode, ok: resp.IsSuccessStatusCode, latency_ms: sw.ElapsedMilliseconds, error: (string?)null);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "ML smoke-test failed for {Service}", name);
                return (name, status: 0, ok: false, latency_ms: sw.ElapsedMilliseconds, error: ex.GetType().Name);
            }
        });

        foreach (var r in await Task.WhenAll(tasks))
            results[r.name] = new { status = r.status, ok = r.ok, latency_ms = r.latency_ms, error = r.error };

        var healthy = results.Values.Cast<dynamic>().Count(v => (bool)v.ok);
        return Ok(new
        {
            total = MlServiceNames.Length,
            healthy,
            unhealthy = MlServiceNames.Length - healthy,
            services = results,
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] AdminUserFilter filter, CancellationToken ct)
    {
        var q = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Role) && Enum.TryParse<Role>(filter.Role, ignoreCase: true, out var role))
            q = q.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(filter.Region))
            q = q.Where(u => u.AssignedRegion == filter.Region);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            q = q.Where(u => u.Email.ToLower().Contains(s) || (u.FullName != null && u.FullName.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 500);

        var items = await q.OrderBy(u => u.Email)
            .Skip((page - 1) * size).Take(size)
            .Select(u => new AdminUserDto(
                u.Id, u.Email, u.FullName, u.Role.ToString().ToLowerInvariant(),
                u.AssignedRegion, u.TelegramChatId != null, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

        return Ok(new PagedResultDto<AdminUserDto>(items, total, page, size));
    }

    [HttpPatch("users/{userId:guid}/assign-region")]
    public async Task<IActionResult> AssignRegion(Guid userId, [FromBody] AssignRegionRequest body, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found");

        user.AssignedRegion = string.IsNullOrWhiteSpace(body.Region) ? null : body.Region.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new AdminUserDto(
            user.Id, user.Email, user.FullName, user.Role.ToString().ToLowerInvariant(),
            user.AssignedRegion, user.TelegramChatId != null, user.IsActive, user.CreatedAt));
    }

    [HttpPost("demo/reset")]
    public async Task<IActionResult> ResetDemo(CancellationToken ct) =>
        Ok(await seed.RunAsync(ct));
}
