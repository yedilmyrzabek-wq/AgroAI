using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Alert
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public AlertType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public Guid? FarmId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
