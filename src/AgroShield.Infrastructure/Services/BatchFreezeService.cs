using AgroShield.Application.Auth;
using AgroShield.Application.DTOs.SupplyChain;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class BatchFreezeService(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IHttpContextAccessor httpContext,
    IBackgroundJobClient jobs) : IBatchFreezeService
{
    public async Task<FreezeBatchResponse> FreezeAsync(Guid batchId, string reason, CancellationToken ct = default)
    {
        EnsureInspectorOrAdmin();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10 || reason.Length > 1000)
            throw new Domain.Exceptions.ValidationException("reason must be 10..1000 characters");

        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException("Batch not found");

        EnsureRegionAccess(batch.Farm.Region);

        if (batch.Status == "frozen")
            throw new ConflictException("Batch already frozen", "already_frozen");
        if (batch.Status is "delivered" or "disputed" or "completed")
            throw new ConflictException($"Batch status '{batch.Status}' cannot be frozen", "not_active");

        var actor = await GetActorAsync(ct);
        var now = DateTime.UtcNow;

        batch.Status = "frozen";
        batch.FrozenAt = now;
        batch.FrozenBy = actor.Id;
        batch.FreezeReason = reason;

        var auditId = await WriteAuditAsync(batch.Id, "freeze", actor.Id, reason, MetadataIp(), ct);

        var related = await db.Subsidies
            .Where(s => s.FarmId == batch.FarmId
                     && (s.Status == SubsidyStatus.Pending || s.Status == SubsidyStatus.Approved))
            .ToListAsync(ct);
        foreach (var s in related) s.Status = SubsidyStatus.UnderReview;

        await db.SaveChangesAsync(ct);

        jobs.Enqueue<INotificationDispatcher>(d => d.SendBatchFrozenAsync(batch.Id));

        return new FreezeBatchResponse(
            BatchId: batch.Id,
            Status: batch.Status,
            FrozenAt: now,
            FrozenBy: new FreezeActorDto(actor.Id, actor.FullName ?? actor.Email),
            Reason: reason,
            AuditLogId: auditId,
            NotificationSent: new NotificationStatusDto(Telegram: false, Email: false));
    }

    public async Task<UnfreezeBatchResponse> UnfreezeAsync(Guid batchId, string reason, CancellationToken ct = default)
    {
        EnsureInspectorOrAdmin();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10 || reason.Length > 1000)
            throw new Domain.Exceptions.ValidationException("reason must be 10..1000 characters");

        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException("Batch not found");

        EnsureRegionAccess(batch.Farm.Region);

        if (batch.Status != "frozen")
            throw new ConflictException("Batch is not frozen");

        var actor = await GetActorAsync(ct);
        var now = DateTime.UtcNow;

        batch.Status = "active";
        batch.UnfrozenAt = now;
        batch.UnfrozenBy = actor.Id;

        var auditId = await WriteAuditAsync(batch.Id, "unfreeze", actor.Id, reason, MetadataIp(), ct);
        await db.SaveChangesAsync(ct);

        return new UnfreezeBatchResponse(
            BatchId: batch.Id,
            Status: batch.Status,
            UnfrozenAt: now,
            UnfrozenBy: new FreezeActorDto(actor.Id, actor.FullName ?? actor.Email),
            Reason: reason,
            AuditLogId: auditId);
    }

    public async Task<AuditLogResponse> GetAuditLogAsync(Guid batchId, CancellationToken ct = default)
    {
        EnsureInspectorOrAdmin();

        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException("Batch not found");
        EnsureRegionAccess(batch.Farm.Region);

        var rows = await db.SupplyChainAuditLogs
            .Where(a => a.BatchId == batchId)
            .OrderByDescending(a => a.PerformedAt)
            .Join(db.Users, a => a.PerformedBy, u => u.Id,
                (a, u) => new { Audit = a, User = u })
            .ToListAsync(ct);

        var items = rows.Select(r => new AuditLogItemDto(
            r.Audit.Id,
            r.Audit.Action,
            r.Audit.PerformedAt,
            new AuditLogActorDto(r.User.Id, r.User.FullName ?? r.User.Email, r.User.Role.ToString().ToLowerInvariant()),
            r.Audit.Reason,
            ParseMetadata(r.Audit.MetadataJson))).ToList();

        return new AuditLogResponse(batchId, items);
    }

    public async Task<FreezeClusterResponse> FreezeClusterAsync(Guid anomalyId, string reason, CancellationToken ct = default)
    {
        EnsureInspectorOrAdmin();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10 || reason.Length > 1000)
            throw new Domain.Exceptions.ValidationException("reason must be 10..1000 characters");

        var anomaly = await db.Anomalies.FirstOrDefaultAsync(a => a.Id == anomalyId, ct)
            ?? throw new NotFoundException("Anomaly not found");

        var farmIds = (anomaly.RelatedFarmIds ?? Array.Empty<Guid>()).Append(anomaly.FarmId).Distinct().ToArray();

        var batches = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .Where(b => farmIds.Contains(b.FarmId))
            .Take(100)
            .ToListAsync(ct);

        var actor = await GetActorAsync(ct);
        var now = DateTime.UtcNow;

        var frozenIds = new List<Guid>();
        var auditIds = new List<Guid>();
        var skipped = 0;
        var perFarm = new Dictionary<Guid, int>();

        foreach (var batch in batches)
        {
            if (batch.Status != "active") { skipped++; continue; }
            // skip outside inspector region
            if (currentUser.Role == Role.Inspector && currentUser.Region is not null && batch.Farm.Region != currentUser.Region)
            { skipped++; continue; }

            batch.Status = "frozen";
            batch.FrozenAt = now;
            batch.FrozenBy = actor.Id;
            batch.FreezeReason = reason;

            var audit = new SupplyChainAuditLog
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Action = "cluster_freeze",
                PerformedBy = actor.Id,
                PerformedAt = now,
                Reason = reason,
                MetadataJson = JsonSerializer.Serialize(new { anomaly_id = anomalyId, ip = MetadataIp() }),
            };
            db.SupplyChainAuditLogs.Add(audit);

            frozenIds.Add(batch.Id);
            auditIds.Add(audit.Id);
            perFarm[batch.FarmId] = perFarm.GetValueOrDefault(batch.FarmId) + 1;
        }

        anomaly.FrozenBatchesCount = frozenIds.Count;
        anomaly.LastFreezeAt = now;

        await db.SaveChangesAsync(ct);

        foreach (var id in frozenIds)
            jobs.Enqueue<INotificationDispatcher>(d => d.SendBatchFrozenAsync(id));

        var farmsInfo = await db.Farms
            .Where(f => perFarm.Keys.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var affected = farmsInfo.Select(f => new AffectedFarmDto(f.Id, f.Name, perFarm[f.Id])).ToList();

        return new FreezeClusterResponse(
            anomalyId,
            frozenIds.Count,
            skipped,
            affected,
            frozenIds,
            auditIds,
            NotificationsSent: affected.Count);
    }

    private void EnsureInspectorOrAdmin()
    {
        if (currentUser.Role != Role.Inspector && currentUser.Role != Role.Admin)
            throw new ForbiddenException("Inspector or Admin role required");
    }

    private void EnsureRegionAccess(string farmRegion)
    {
        if (currentUser.Role == Role.Inspector
            && !string.IsNullOrEmpty(currentUser.Region)
            && !string.Equals(farmRegion, currentUser.Region, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Farm is outside your assigned region");
    }

    private async Task<User> GetActorAsync(CancellationToken ct) =>
        await db.Users.FindAsync([currentUser.UserId], ct)
            ?? throw new UnauthorizedException("Acting user not found");

    private string? MetadataIp() =>
        httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private async Task<Guid> WriteAuditAsync(Guid batchId, string action, Guid userId, string reason, string? ip, CancellationToken ct)
    {
        var entry = new SupplyChainAuditLog
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Action = action,
            PerformedBy = userId,
            PerformedAt = DateTime.UtcNow,
            Reason = reason,
            MetadataJson = JsonSerializer.Serialize(new { ip_address = ip }),
        };
        db.SupplyChainAuditLogs.Add(entry);
        return await Task.FromResult(entry.Id);
    }

    private static object? ParseMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return null; }
    }
}
