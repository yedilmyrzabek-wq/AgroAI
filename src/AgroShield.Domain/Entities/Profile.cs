using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Profile
{
    public Guid UserId { get; set; }
    public Role Role { get; set; }
    public string FullName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public long? TelegramChatId { get; set; }
    public Guid? FarmId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Farm> OwnedFarms { get; set; } = [];
    public ICollection<Alert> Alerts { get; set; } = [];
    public ICollection<ChatSession> ChatSessions { get; set; } = [];
}
