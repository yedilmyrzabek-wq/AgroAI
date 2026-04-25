namespace AgroShield.Application.DTOs.Sensors;

public class RfidScanDto
{
    public string DeviceId { get; set; } = null!;
    public Guid FarmId { get; set; }
    public string RfidTag { get; set; } = null!;
    public DateTime? ScannedAt { get; set; }
}
