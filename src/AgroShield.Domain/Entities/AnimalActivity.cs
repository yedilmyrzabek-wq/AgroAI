namespace AgroShield.Domain.Entities;

public class AnimalActivity
{
    public long Id { get; set; }
    public Guid AnimalId { get; set; }
    public string DeviceId { get; set; } = null!;
    public DateTime DetectedAt { get; set; }

    public Animal Animal { get; set; } = null!;
}
