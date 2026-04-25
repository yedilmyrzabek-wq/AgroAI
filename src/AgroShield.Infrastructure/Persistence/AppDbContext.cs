using AgroShield.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgroShield.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<AnimalActivity> AnimalActivities => Set<AnimalActivity>();
    public DbSet<Subsidy> Subsidies => Set<Subsidy>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<PlantDiagnosis> PlantDiagnoses => Set<PlantDiagnosis>();
    public DbSet<SupplyChainNode> SupplyChainNodes => Set<SupplyChainNode>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
