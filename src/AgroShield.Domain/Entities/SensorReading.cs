namespace AgroShield.Domain.Entities;

public class SensorReading
{
    public long Id { get; set; }
    public Guid FarmId { get; set; }
    public string DeviceId { get; set; } = null!;
    public decimal Temp { get; set; }
    public decimal Humidity { get; set; }
    public int Light { get; set; }
    public bool Fire { get; set; }
    public int WaterLevel { get; set; }
    public DateTime RecordedAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
