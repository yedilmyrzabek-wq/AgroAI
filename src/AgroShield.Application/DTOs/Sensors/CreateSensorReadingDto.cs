namespace AgroShield.Application.DTOs.Sensors;

public class CreateSensorReadingDto
{
    public string DeviceId { get; set; } = null!;
    public Guid? FarmId { get; set; }
    public decimal Temp { get; set; }
    public decimal Humidity { get; set; }
    public int Light { get; set; }
    public bool Fire { get; set; }
    public int WaterLevel { get; set; }
    public DateTime? RecordedAt { get; set; }
}
