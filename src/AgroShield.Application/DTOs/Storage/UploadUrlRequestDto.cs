namespace AgroShield.Application.DTOs.Storage;

public class UploadUrlRequestDto
{
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
}
