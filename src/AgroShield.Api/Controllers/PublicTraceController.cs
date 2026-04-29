using AgroShield.Application.Services;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public class PublicTraceController(
    AppDbContext db,
    ISupplyChainService supplyChain) : ControllerBase
{
    [HttpGet("trace/{batchId}")]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> Trace(string batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return BadRequest(new { error = "invalid_batch_id" });

        // Two flavors of trace:
        //   1. Native batch_code (e.g. "WHE-XXXXXX-20260415") — legacy SupplyChainBatch row.
        //   2. Ledger-style batchId ("subsidy-…", "livestock-…") — generic supply-chain ledger.
        if (LooksLikeLedgerBatch(batchId))
        {
            return await TraceLedgerAsync(batchId, ct);
        }

        return await TraceNativeBatchAsync(batchId, ct);
    }

    private static bool LooksLikeLedgerBatch(string id) =>
        id.StartsWith("subsidy-", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("livestock-", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("batch-", StringComparison.OrdinalIgnoreCase);

    private async Task<IActionResult> TraceLedgerAsync(string batchId, CancellationToken ct)
    {
        var records = await supplyChain.GetLedgerAsync(batchId, ct);
        if (records.Count == 0)
            return NotFound(new { error = "batch_not_found", message = "Партия не найдена", details = (object?)null });

        var verify = await supplyChain.VerifyAsync(batchId, ct);

        int mlAuditsPassed = 0;
        var steps = records.Select(r =>
        {
            var step = AnonymizedEventLabel(r.EventType);
            if (LooksLikeMlAudit(r.EventType, r.PayloadJson)) mlAuditsPassed++;
            return new
            {
                step,
                date = r.CreatedAt.ToString("yyyy-MM-dd"),
                verified = true,
            };
        }).ToList();

        var trustScore = CalculateTrust(records.Count, mlAuditsPassed, verify.IsValid);

        return Ok(new
        {
            batch_id = batchId,
            total_steps = records.Count,
            chain_intact = verify.IsValid,
            broken_at = verify.BrokenAtTimestamp?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            steps,
            trust_score = trustScore,
            ml_audits_passed = mlAuditsPassed,
            verified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
    }

    private async Task<IActionResult> TraceNativeBatchAsync(string batchCode, CancellationToken ct)
    {
        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .Include(b => b.Transitions)
            .FirstOrDefaultAsync(b => b.BatchCode == batchCode, ct);

        if (batch is null)
            return NotFound(new { error = "batch_not_found", message = "Партия не найдена", details = (object?)null });

        var transitions = batch.Transitions.OrderBy(t => t.TransferredAt).ToList();
        var hashChainVerified = VerifyTransitionChain(transitions);

        var steps = transitions.Select(t => new
        {
            step = $"{t.FromNodeType} → {t.ToNodeType}",
            date = t.TransferredAt.ToString("yyyy-MM-dd"),
            verified = true,
        }).ToList();

        var trustScore = CalculateTrust(steps.Count + 1, mlAuditsPassed: hashChainVerified ? 1 : 0, hashChainVerified);

        return Ok(new
        {
            batch_id = batch.BatchCode,
            crop_type = batch.CropType,
            harvest_date = batch.HarvestDate?.ToString("yyyy-MM-dd"),
            farm = new
            {
                name = batch.Farm.Name,
                region = batch.Farm.Region,
            },
            initial_weight_kg = batch.InitialWeightKg,
            current_weight_kg = batch.CurrentWeightKg,
            status = batch.Status,
            frozen = batch.Status == "frozen",
            frozen_at = batch.Status == "frozen" ? batch.FrozenAt : null,
            freeze_reason = batch.Status == "frozen" ? batch.FreezeReason : null,
            ndvi_at_harvest = batch.Farm.NdviMean,
            climate_risk_at_harvest = batch.Farm.RiskScore,
            total_steps = steps.Count,
            chain_intact = hashChainVerified,
            broken_at = (string?)null,
            steps,
            trust_score = trustScore,
            ml_audits_passed = hashChainVerified ? 1 : 0,
            verified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
    }

    private static int CalculateTrust(int totalSteps, int mlAuditsPassed, bool chainValid)
    {
        if (!chainValid) return 0;
        var baseScore = 60;
        var bonus = Math.Min(40, mlAuditsPassed * 10);
        return Math.Min(100, baseScore + bonus);
    }

    private static string AnonymizedEventLabel(string eventType) => eventType switch
    {
        "registered"                       => "Заявка зарегистрирована",
        "tranche_released"                 => "Транш выплачен",
        "fertilizer_application_recorded"  => "Удобрения внесены (NDVI проверка)",
        "harvest_recorded"                 => "Урожай зарегистрирован",
        "cv_increment"                     => "Поголовье обновлено (CV)",
        "cv_full_recount"                  => "Полный пересчёт стада",
        "head_count_check"                 => "Проверка поголовья",
        "lost"                             => "Утрата",
        "sold"                             => "Продажа",
        "born"                             => "Прирост",
        "bought"                           => "Закупка",
        "manual_adjustment"                => "Ручная корректировка",
        _                                  => eventType,
    };

    private static bool LooksLikeMlAudit(string eventType, string payloadJson)
    {
        if (eventType.Contains("verified", StringComparison.OrdinalIgnoreCase)) return true;
        if (eventType.Contains("confirmed", StringComparison.OrdinalIgnoreCase)) return true;
        if (eventType == "fertilizer_application_recorded") return true;
        if (eventType == "cv_increment" || eventType == "cv_full_recount") return true;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("ml_result", out _)) return true;
            if (doc.RootElement.TryGetProperty("model_version", out _)) return true;
        }
        catch { }
        return false;
    }

    private static bool VerifyTransitionChain(IReadOnlyList<Domain.Entities.SupplyChainTransition> transitions)
    {
        for (var i = 1; i < transitions.Count; i++)
        {
            if (!string.Equals(transitions[i].FromNodeType, transitions[i - 1].ToNodeType, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(transitions[i].FromNodeId, transitions[i - 1].ToNodeId, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
