using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public class PublicTraceController(AppDbContext db) : ControllerBase
{
    [HttpGet("trace/{batchCode}")]
    public async Task<IActionResult> Trace(string batchCode, CancellationToken ct)
    {
        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .Include(b => b.Transitions)
            .FirstOrDefaultAsync(b => b.BatchCode == batchCode, ct);

        if (batch is null)
            return NotFound(new { error = "not_found", message = "Batch not found", details = (object?)null });

        var transitions = batch.Transitions.OrderBy(t => t.TransferredAt).ToList();

        var timeline = transitions.Select(t => new
        {
            from = $"{ResolveNodeName(t.FromNodeType, t.FromNodeId, batch.Farm.Name)} ({t.FromNodeType})",
            to = $"{ResolveNodeName(t.ToNodeType, t.ToNodeId, batch.Farm.Name)} ({t.ToNodeType})",
            weightKg = t.WeightKg,
            transferredAt = t.TransferredAt,
        });

        var hashChainVerified = VerifyChain(transitions);

        var response = new
        {
            batchCode = batch.BatchCode,
            cropType = batch.CropType,
            harvestDate = batch.HarvestDate,
            farm = new
            {
                name = batch.Farm.Name,
                region = batch.Farm.Region,
                district = batch.Farm.District,
            },
            initialWeightKg = batch.InitialWeightKg,
            currentWeightKg = batch.CurrentWeightKg,
            currentHolder = new
            {
                type = batch.CurrentHolderType,
                name = ResolveNodeName(batch.CurrentHolderType, batch.CurrentHolderId, batch.Farm.Name),
            },
            status = batch.Status,
            frozen = batch.Status == "frozen",
            frozenAt = batch.Status == "frozen" ? batch.FrozenAt : null,
            freezeReason = batch.Status == "frozen" ? batch.FreezeReason : null,
            ndviAtHarvest = batch.Farm.NdviMean,
            climateRiskAtHarvest = batch.Farm.RiskScore,
            timeline,
            hashChainVerified,
            verifiedAt = DateTime.UtcNow,
        };

        return Ok(response);
    }

    private static string ResolveNodeName(string type, string? id, string farmName) => type switch
    {
        "farm" => farmName,
        "elevator" => $"Элеватор {id?[..Math.Min(8, id?.Length ?? 0)] ?? "unknown"}",
        "transit" => $"Транспорт {id?[..Math.Min(8, id?.Length ?? 0)] ?? "unknown"}",
        _ => id ?? "unknown",
    };

    private static bool VerifyChain(IReadOnlyList<Domain.Entities.SupplyChainTransition> transitions)
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
