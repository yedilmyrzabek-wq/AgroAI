namespace AgroShield.Domain.Entities;

public class WeeklyReport
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid[] FarmIds { get; set; } = [];
    public string ReportMarkdown { get; set; } = null!;
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
