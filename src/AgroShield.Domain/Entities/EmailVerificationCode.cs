namespace AgroShield.Domain.Entities;

public class EmailVerificationCode
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
