using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public Role Role { get; set; } = Role.Farmer;
    public long? TelegramChatId { get; set; }
    public string? TelegramUsername { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? AssignedRegion { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Farm> OwnedFarms { get; set; } = [];
    public ICollection<Alert> Alerts { get; set; } = [];
    public ICollection<ChatSession> ChatSessions { get; set; } = [];
}
