using AgroShield.Application.DTOs.Farms;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/farms")]
[Authorize]
public class FarmsController(IFarmService farms) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] FarmFilterDto filter, CancellationToken ct) =>
        Ok(await farms.GetAllAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try { return Ok(await farms.GetByIdAsync(id, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Roles = "Inspector,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateFarmDto dto, CancellationToken ct)
    {
        var farm = await farms.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = farm.Id }, farm);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmDto dto, CancellationToken ct)
    {
        try { return Ok(await farms.UpdateAsync(id, dto, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await farms.DeleteAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = "not_found", message = ex.Message }); }
    }
}
