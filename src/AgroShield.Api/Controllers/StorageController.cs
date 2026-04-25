using AgroShield.Application.DTOs.Storage;
using AgroShield.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/storage")]
[Authorize]
public class StorageController(IStorageService storage) : ControllerBase
{
    [HttpPost("upload-url")]
    public async Task<IActionResult> GetUploadUrl([FromBody] UploadUrlRequestDto dto)
    {
        var ext = Path.GetExtension(dto.FileName);
        var key = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}{ext}";
        var expiry = TimeSpan.FromMinutes(15);

        var url = await storage.GetPresignedUploadUrlAsync(key, dto.ContentType, expiry);

        return Ok(new
        {
            url,
            key,
            expires_at = DateTime.UtcNow.Add(expiry),
        });
    }
}
