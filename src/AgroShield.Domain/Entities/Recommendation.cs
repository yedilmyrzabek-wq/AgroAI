using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Recommendation
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public RecommendationPriority Priority { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public RecommendationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
