namespace AgroShield.Domain.Entities;

public class Farm
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = null!;
    public string Region { get; set; } = null!;
    public string District { get; set; } = null!;
    public decimal AreaHectares { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string CropType { get; set; } = null!;
    public string? DeviceId { get; set; }
    public string PolygonGeoJson { get; set; } = "{}";
    public int RiskScore { get; set; }
    public decimal? NdviMean { get; set; }
    public decimal? ActiveAreaFromNdvi { get; set; }
    public DateTime? NdviUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public ICollection<Sensor> Sensors { get; set; } = [];
    public ICollection<Animal> Animals { get; set; } = [];
    public ICollection<Subsidy> Subsidies { get; set; } = [];
    public ICollection<Anomaly> Anomalies { get; set; } = [];
    public ICollection<PlantDiagnosis> PlantDiagnoses { get; set; } = [];
    public ICollection<Recommendation> Recommendations { get; set; } = [];
}
