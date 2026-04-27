namespace AgroShield.Domain.Entities;

public class NotificationSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string NotificationType { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
