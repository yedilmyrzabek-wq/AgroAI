using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Animal
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string? RfidTag { get; set; }
    public string Species { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public AnimalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<AnimalActivity> Activities { get; set; } = [];
}
