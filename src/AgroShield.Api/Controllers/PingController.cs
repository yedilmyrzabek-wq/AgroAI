using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() =>
        Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
