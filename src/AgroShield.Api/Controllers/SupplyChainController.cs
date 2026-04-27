using AgroShield.Application.DTOs.SupplyChain;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/supply-chain")]
[Authorize]
public class SupplyChainController(IBatchFreezeService freeze) : ControllerBase
{
    [HttpPost("batch/{id:guid}/freeze")]
    public async Task<IActionResult> Freeze(Guid id, [FromBody] FreezeBatchRequest body, CancellationToken ct) =>
        Ok(await freeze.FreezeAsync(id, body.Reason, ct));

    [HttpPost("batch/{id:guid}/unfreeze")]
    public async Task<IActionResult> Unfreeze(Guid id, [FromBody] UnfreezeBatchRequest body, CancellationToken ct) =>
        Ok(await freeze.UnfreezeAsync(id, body.Reason, ct));

    [HttpGet("batch/{id:guid}/audit-log")]
    public async Task<IActionResult> AuditLog(Guid id, CancellationToken ct) =>
        Ok(await freeze.GetAuditLogAsync(id, ct));

    [HttpPost("freeze-cluster")]
    public async Task<IActionResult> FreezeCluster([FromBody] FreezeClusterRequest body, CancellationToken ct) =>
        Ok(await freeze.FreezeClusterAsync(body.AnomalyId, body.Reason, ct));
}
