using AgroShield.Application.DTOs.SupplyChain;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class SupplyChainService(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<SupplyChainService> logger) : ISupplyChainService
{
    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<SupplyChainLedgerRecordDto> AppendAsync(
        string batchId,
        string eventType,
        object payload,
        Guid? actorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new ArgumentException("batchId is required", nameof(batchId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType is required", nameof(eventType));

        var prevHash = await db.SupplyChainLedger
            .Where(e => e.BatchId == batchId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(ct) ?? ZeroHash;

        var now = DateTime.UtcNow;
        var payloadJson = JsonSerializer.Serialize(payload, SnakeOpts);
        var entryHash = ComputeHash(prevHash, batchId, eventType, payloadJson, now);

        var entry = new SupplyChainLedgerEntry
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            EventType = eventType,
            PayloadJson = payloadJson,
            ActorId = actorId,
            PrevHash = prevHash,
            EntryHash = entryHash,
            CreatedAt = now,
        };

        db.SupplyChainLedger.Add(entry);
        await db.SaveChangesAsync(ct);

        // Mirror to external SupplyChainTracker (best-effort, non-blocking)
        _ = MirrorAsync(entry, payload);

        return Map(entry);
    }

    public async Task<List<SupplyChainLedgerRecordDto>> GetLedgerAsync(string batchId, CancellationToken ct = default)
    {
        var entries = await db.SupplyChainLedger
            .Where(e => e.BatchId == batchId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        return entries.Select(Map).ToList();
    }

    public async Task<VerifyChainResult> VerifyAsync(string batchId, CancellationToken ct = default)
    {
        var entries = await db.SupplyChainLedger
            .Where(e => e.BatchId == batchId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var prev = ZeroHash;
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.PrevHash != prev)
                return new VerifyChainResult(false, entries.Count, e.CreatedAt, i);
            var expected = ComputeHash(prev, e.BatchId, e.EventType, e.PayloadJson, e.CreatedAt);
            if (expected != e.EntryHash)
                return new VerifyChainResult(false, entries.Count, e.CreatedAt, i);
            prev = e.EntryHash;
        }
        return new VerifyChainResult(true, entries.Count, null, null);
    }

    private async Task MirrorAsync(SupplyChainLedgerEntry entry, object payload)
    {
        try
        {
            var client = factory.CreateClient("SupplyChainTracker");
            var body = new
            {
                batch_id = entry.BatchId,
                event_type = entry.EventType,
                actor_id = entry.ActorId,
                timestamp = entry.CreatedAt,
                prev_hash = entry.PrevHash,
                hash = entry.EntryHash,
                payload,
            };
            var resp = await client.PostAsJsonAsync("/append-record", body, SnakeOpts);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("SupplyChainTracker /append-record returned {Status} for batch {Batch}", resp.StatusCode, entry.BatchId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SupplyChainTracker mirror failed for batch {Batch} (non-blocking)", entry.BatchId);
        }
    }

    private static string ComputeHash(string prevHash, string batchId, string eventType, string payloadJson, DateTime at)
    {
        var input = $"{prevHash}|{batchId}|{eventType}|{payloadJson}|{at:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static SupplyChainLedgerRecordDto Map(SupplyChainLedgerEntry e) =>
        new(e.Id, e.BatchId, e.EventType, e.PayloadJson, e.ActorId, e.PrevHash, e.EntryHash, e.CreatedAt);
}
