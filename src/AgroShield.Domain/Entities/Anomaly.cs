using AgroShield.Domain.Enums;

namespace AgroShield.Domain.Entities;

public class Anomaly
{
    public Guid Id { get; set; }
    public AnomalyType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid FarmId { get; set; }
    public int RiskScore { get; set; }
    public string[] Reasons { get; set; } = [];
    public AnomalyStatus Status { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }

    public int? GraphRiskScore { get; set; }
    public Guid[]? RelatedFarmIds { get; set; }
    public string? MlFeaturesJson { get; set; }

    public int FrozenBatchesCount { get; set; }
    public DateTime? LastFreezeAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
