using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Sensor
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public SensorType Type { get; set; }
    public string SerialNumber { get; set; } = null!;
    public DateTime InstalledAt { get; set; }
    public bool IsActive { get; set; }

    public Farm Farm { get; set; } = null!;
}
