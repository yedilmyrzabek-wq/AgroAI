using AgroShield.Application.DTOs.Admin;
using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgroShield.Infrastructure.Persistence;

public class IdealDemoSeed(
    AppDbContext db,
    IPasswordHasher hasher,
    IConfiguration config,
    ILogger<IdealDemoSeed> logger) : IDemoSeedService
{
    public async Task<DemoResetSummary> RunAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogWarning("IdealDemoSeed: TRUNCATE all demo tables");

        // Order matters because of FKs (cascade is mostly set, but be explicit).
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
              supply_chain_audit_log,
              supply_chain_transitions,
              supply_chain_batches,
              supply_chain_nodes,
              supply_chain_ledger,
              livestock_ledger,
              livestock,
              fertilizer_recommendations,
              plant_diagnoses,
              recommendations,
              anomalies,
              subsidy_tranches,
              subsidies,
              alerts,
              animal_activities,
              animals,
              sensor_readings,
              sensors,
              weekly_reports,
              notification_subscriptions,
              chat_messages,
              chat_sessions,
              farms,
              email_verification_codes,
              refresh_tokens,
              users,
              knowledge_chunks
            RESTART IDENTITY CASCADE;
            """, ct);

        var demoChatId = long.TryParse(config["Demo:FarmerChatId"] ?? Environment.GetEnvironmentVariable("DEMO_FARMER_CHAT_ID"), out var raw) ? raw : (long?)null;

        // ── Users ────────────────────────────────────────────────────────
        var inspector = MakeUser("inspector.demo@agroshield.kz", "Айгуль Серикова", Role.Inspector, region: "Северо-Казахстанская");
        var farmer    = MakeUser("farmer.demo@agroshield.kz",    "Бекзат Мукашев",   Role.Farmer,    chatId: demoChatId);
        var shadow    = MakeUser("shadow.farmer@agroshield.kz",  "Тимур Ескендир",  Role.Farmer);
        var admin     = MakeUser("admin@agroshield.kz",          "Администратор",   Role.Admin);

        db.Users.AddRange(inspector, farmer, shadow, admin);
        await db.SaveChangesAsync(ct);

        // ── Farms ────────────────────────────────────────────────────────
        const string ownerIinDemo = "111111111111";
        const string ownerIinShadow = "222222222222";
        const string bankBinDemo = "940140000017";
        const string bankBinShadow = "950440008912";
        var sharedElevator = Guid.NewGuid();

        var farms = new List<Farm>
        {
            MakeFarm("Қызылжар-Агро",  farmer, "Северо-Казахстанская", "Петропавловск", 54.872, 69.155, 1200, "wheat",        0.72m, 12, ownerIinDemo,   bankBinDemo),
            MakeFarm("Ишим-Колос",     farmer, "Северо-Казахстанская", "Тайынша",       53.847, 69.751, 800,  "barley",       0.65m, 18, ownerIinDemo,   bankBinDemo),
            MakeFarm("Акмола-Степь",   farmer, "Акмолинская",          "Кокшетау",      53.282, 69.402, 2400, "rapeseed",     0.68m, 25, ownerIinDemo,   bankBinDemo),
            MakeFarm("Ұлы-Дала",       farmer, "Северо-Казахстанская", "Тимирязево",    54.103, 67.642, 600,  "flax",         0.61m, 35, ownerIinDemo,   bankBinDemo),
            MakeFarm("Степь-Север",    shadow, "Северо-Казахстанская", "Булаево",       54.910, 70.430, 3000, "wheat",        0.28m, 87, ownerIinShadow, bankBinShadow, sharedElevator),
            MakeFarm("Зерно-Сити",     shadow, "Северо-Казахстанская", "Мамлютка",      54.872, 68.481, 1800, "wheat",        0.31m, 79, ownerIinShadow, bankBinShadow, sharedElevator),
        };
        db.Farms.AddRange(farms);
        await db.SaveChangesAsync(ct);

        var (farmKzj, farmIsh, farmAkmola, farmUli, farmStep, farmZerno) = (farms[0], farms[1], farms[2], farms[3], farms[4], farms[5]);

        // ── Sensors + 30 days readings ──────────────────────────────────
        var sensors = new List<Sensor>();
        var readings = new List<SensorReading>();
        var rng = new Random(2026);

        foreach (var farm in farms)
        {
            sensors.Add(new Sensor
            {
                Id = Guid.NewGuid(), FarmId = farm.Id, Type = SensorType.DHT11,
                SerialNumber = $"SN-{rng.Next(10000, 99999)}", InstalledAt = Utc(-90), IsActive = true,
            });

            for (var d = 0; d < 30; d++)
            {
                var when = DateTime.UtcNow.AddDays(-d).AddHours(-rng.Next(0, 23));
                var temp = (decimal)(-5 + rng.NextDouble() * 27);
                if (farm.Id == farmStep.Id && (d == 1 || d == 2)) temp = -3m;
                readings.Add(new SensorReading
                {
                    FarmId = farm.Id, DeviceId = farm.DeviceId!,
                    Temp = temp,
                    Humidity = (decimal)(35 + rng.NextDouble() * 40),
                    Light = rng.Next(200, 901),
                    Fire = false,
                    WaterLevel = rng.Next(150, 800),
                    RecordedAt = when,
                });
            }
        }
        db.Sensors.AddRange(sensors);
        await db.SaveChangesAsync(ct);
        foreach (var batch in readings.Chunk(500))
        {
            db.SensorReadings.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }

        // ── Plant diagnoses ─────────────────────────────────────────────
        var diagnoses = new List<PlantDiagnosis>
        {
            Diagnose(farmKzj.Id, farmer.Id, "Healthy",            "Здорово",            true,  "low",    0.95m, "Плановое наблюдение."),
            Diagnose(farmKzj.Id, farmer.Id, "Healthy",            "Здорово",            true,  "low",    0.94m, "Состояние нормальное."),
            Diagnose(farmKzj.Id, farmer.Id, "Healthy",            "Здорово",            true,  "low",    0.93m, "Без отклонений."),
            Diagnose(farmIsh.Id, farmer.Id, "Powdery_mildew",     "Мучнистая роса",     false, "medium", 0.84m, "Триазольные фунгициды."),
            Diagnose(farmStep.Id, shadow.Id, "Septoria_tritici", "Септориоз пшеницы",   false, "high",   0.88m, "Срочная обработка фунгицидами."),
            Diagnose(farmStep.Id, shadow.Id, "Septoria_tritici", "Септориоз пшеницы",   false, "high",   0.86m, "Повтор обработки через 10 дней."),
            Diagnose(farmZerno.Id, shadow.Id, "Yellow_rust",     "Жёлтая ржавчина",     false, "high",   0.91m, "Триазольные фунгициды + мониторинг."),
            new() {
                Id = Guid.NewGuid(), FarmId = farmZerno.Id, UserId = shadow.Id,
                Disease = "Unknown", DiseaseRu = "Неопределено", Confidence = 0.42m,
                Severity = "low", IsHealthy = false, Recommendation = "Требуется ручная проверка инспектора.",
                ImageUrl = "", ModelVersion = "plant-cv-v2.1", CreatedAt = DateTime.UtcNow.AddDays(-2),
            },
        };
        db.PlantDiagnoses.AddRange(diagnoses);
        await db.SaveChangesAsync(ct);

        // ── Anomalies (with cluster relations) ──────────────────────────
        var anomStep = new Anomaly
        {
            Id = Guid.NewGuid(), FarmId = farmStep.Id, EntityType = AnomalyType.Subsidy, EntityId = farmStep.Id,
            RiskScore = 87, Status = AnomalyStatus.Active,
            Reasons = ["Общий ИИН с фермой Зерно-Сити", "Совместный банковский BIN", "NDVI-реальность ниже нормы"],
            DetectedAt = Utc(-2), GraphRiskScore = 90, RelatedFarmIds = [farmZerno.Id],
        };
        var anomZerno = new Anomaly
        {
            Id = Guid.NewGuid(), FarmId = farmZerno.Id, EntityType = AnomalyType.Subsidy, EntityId = farmZerno.Id,
            RiskScore = 79, Status = AnomalyStatus.Active,
            Reasons = ["Общий ИИН и BIN с фермой Степь-Север", "Расхождение площади NDVI"],
            DetectedAt = Utc(-2), GraphRiskScore = 82, RelatedFarmIds = [farmStep.Id],
        };
        var anomUli = new Anomaly
        {
            Id = Guid.NewGuid(), FarmId = farmUli.Id, EntityType = AnomalyType.Ndvi, EntityId = farmUli.Id,
            RiskScore = 58, Status = AnomalyStatus.InProgress,
            Reasons = ["Падение NDVI > 15% за неделю"], DetectedAt = Utc(-3),
        };
        var anomIsh = new Anomaly
        {
            Id = Guid.NewGuid(), FarmId = farmIsh.Id, EntityType = AnomalyType.Sensor, EntityId = farmIsh.Id,
            RiskScore = 32, Status = AnomalyStatus.Active,
            Reasons = ["Сенсор температуры даёт нерегулярные показания"], DetectedAt = Utc(-1),
        };
        db.Anomalies.AddRange(anomStep, anomZerno, anomUli, anomIsh);
        await db.SaveChangesAsync(ct);

        // ── Subsidies ───────────────────────────────────────────────────
        db.Subsidies.AddRange(
            Subsidy(farmKzj.Id,   2_000_000m, 1100m, SubsidyStatus.Approved,    "Субсидии на горюче-смазочные материалы"),
            Subsidy(farmIsh.Id,   1_500_000m, 750m,  SubsidyStatus.Approved,    "Субсидирование удобрений"),
            Subsidy(farmAkmola.Id,3_000_000m, 2300m, SubsidyStatus.Approved,    "Субсидии на приобретение семян"),
            Subsidy(farmUli.Id,     800_000m, 580m,  SubsidyStatus.UnderReview, "Субсидирование стоимости гербицидов"),
            Subsidy(farmStep.Id,  4_500_000m, 2800m, SubsidyStatus.UnderReview, "Субсидии на горюче-смазочные материалы"),
            Subsidy(farmZerno.Id, 3_200_000m, 1700m, SubsidyStatus.UnderReview, "Субсидирование удобрений")
        );
        await db.SaveChangesAsync(ct);

        // ── Tranche-based demo subsidy (TZ v6 §3.5) ─────────────────────
        var demoSubsidy = new Subsidy
        {
            Id = Guid.NewGuid(),
            FarmId = farmStep.Id,
            FarmerId = shadow.Id,
            Amount = 8_000_000m,
            DeclaredArea = farmStep.AreaHectares,
            Purpose = "Программа субсидирования яровых (демо)",
            CropType = farmStep.CropType,
            Status = SubsidyStatus.Approved,
            WorkflowStatus = "in_progress",
            SubmittedAt = Utc(-30),
        };
        var trancheNow = DateTime.UtcNow;
        demoSubsidy.Tranches = new List<SubsidyTranche>
        {
            new() { Id = Guid.NewGuid(), SubsidyId = demoSubsidy.Id, Order = 1, PercentOfTotal = 30m, AmountKzt = 2_400_000m, Status = "released", UnlockCondition = "registered",         ReleasedAt = Utc(-30), CreatedAt = Utc(-30) },
            new() { Id = Guid.NewGuid(), SubsidyId = demoSubsidy.Id, Order = 2, PercentOfTotal = 30m, AmountKzt = 2_400_000m, Status = "released", UnlockCondition = "sowing_confirmed",  ReleasedAt = Utc(-22), CreatedAt = Utc(-30) },
            new() { Id = Guid.NewGuid(), SubsidyId = demoSubsidy.Id, Order = 3, PercentOfTotal = 20m, AmountKzt = 1_600_000m, Status = "pending",  UnlockCondition = "fertilizer_applied", CreatedAt = Utc(-30) },
            new() { Id = Guid.NewGuid(), SubsidyId = demoSubsidy.Id, Order = 4, PercentOfTotal = 20m, AmountKzt = 1_600_000m, Status = "pending",  UnlockCondition = "harvest_recorded",  CreatedAt = Utc(-30) },
        };
        db.Subsidies.Add(demoSubsidy);
        await db.SaveChangesAsync(ct);

        AddSupplyChainLedgerForDemo(demoSubsidy, shadow.Id);

        // ── Livestock for Зерно-Сити (850 sheep) — TZ v6 demo ────────────
        var livestockZerno = new Livestock
        {
            Id = Guid.NewGuid(), FarmId = farmZerno.Id, LivestockType = "sheep",
            DeclaredCount = 850, LastDetectedCount = 850, LastDetectedAt = Utc(-2),
            AnomalyDetected = false, CreatedAt = Utc(-200), UpdatedAt = Utc(-2),
        };
        db.Livestock.Add(livestockZerno);
        AddLedgerChain(farmZerno.Id, "sheep", new[] { 845, 848, 850, 850, 850 }, shadow.Id);

        // ── Supply-chain batches ────────────────────────────────────────
        var batches = new List<SupplyChainBatch>
        {
            Batch(farmKzj,   "wheat",  1000, 1000, "active",    null),
            Batch(farmKzj,   "wheat",  1200, 1200, "active",    null),
            Batch(farmIsh,   "barley", 800,  800,  "delivered", null),
            Batch(farmIsh,   "barley", 700,  700,  "delivered", null),
            Batch(farmStep,  "wheat",  1500, 1500, "frozen",    "Schemes audit СГК — общий ИИН"),
            Batch(farmStep,  "wheat",  1300, 1300, "active",    null),
            Batch(farmStep,  "wheat",  900,  870,  "active",    null),
            Batch(farmZerno, "wheat",  1100, 1100, "active",    null),
        };

        var frozen = batches[4];
        frozen.FrozenAt = Utc(-1);
        frozen.FrozenBy = inspector.Id;

        db.SupplyChainBatches.AddRange(batches);
        await db.SaveChangesAsync(ct);

        db.SupplyChainAuditLogs.Add(new SupplyChainAuditLog
        {
            Id = Guid.NewGuid(), BatchId = frozen.Id, Action = "freeze",
            PerformedBy = inspector.Id, PerformedAt = Utc(-1),
            Reason = frozen.FreezeReason,
            MetadataJson = JsonSerializer.Serialize(new { ip_address = "127.0.0.1", source = "demo_seed" }),
        });

        // ── Livestock + Ledger ──────────────────────────────────────────
        var livestockKzj = new Livestock
        {
            Id = Guid.NewGuid(), FarmId = farmKzj.Id, LivestockType = "cattle",
            DeclaredCount = 100, LastDetectedCount = 98, LastDetectedAt = Utc(-1),
            AnomalyDetected = false, CreatedAt = Utc(-30), UpdatedAt = Utc(-1),
        };
        var livestockStep = new Livestock
        {
            Id = Guid.NewGuid(), FarmId = farmStep.Id, LivestockType = "cattle",
            DeclaredCount = 200, LastDetectedCount = 60, LastDetectedAt = Utc(-1),
            AnomalyDetected = true, CreatedAt = Utc(-30), UpdatedAt = Utc(-1),
        };
        db.Livestock.AddRange(livestockKzj, livestockStep);

        AddLedgerChain(farmKzj.Id, "cattle", new[] { 100, 99, 98, 98, 98 }, farmer.Id);
        AddLedgerChain(farmStep.Id, "cattle", new[] { 200, 180, 140, 90, 60 }, shadow.Id);

        // ── Subscriptions ───────────────────────────────────────────────
        db.NotificationSubscriptions.AddRange(
            Sub(farmer.Id,    "ndvi_drop",       true),
            Sub(farmer.Id,    "weekly_report",   true),
            Sub(farmer.Id,    "fire",            true),
            Sub(farmer.Id,    "daily_digest",    true),
            Sub(inspector.Id, "anomaly",          true),
            Sub(inspector.Id, "livestock_anomaly",true),
            Sub(inspector.Id, "supply_chain_freeze", true)
        );

        // ── Knowledge chunks (5 stub) ───────────────────────────────────
        for (var i = 0; i < 5; i++)
        {
            db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                Id = Guid.NewGuid(),
                SourceDoc = $"МСХ РК Постановление N{100 + i}",
                SourceUrl = $"https://gov.kz/mca/docs/{100 + i}",
                ChunkIndex = i,
                Content = $"Выдержка №{i + 1}: общие требования к субсидиям сельхозпроизводителей региона.",
                CreatedAt = Utc(-15),
            });
        }

        await db.SaveChangesAsync(ct);

        sw.Stop();
        return new DemoResetSummary(
            UsersCreated:           4,
            FarmsCreated:           farms.Count,
            SensorsCreated:         sensors.Count,
            SensorReadingsCreated:  readings.Count,
            DiagnosesCreated:       diagnoses.Count,
            AnomaliesCreated:       4,
            SubsidiesCreated:       7,
            BatchesCreated:         batches.Count,
            LivestockCreated:       3,
            LedgerEntriesCreated:   15,
            SubscriptionsCreated:   7,
            KnowledgeChunksCreated: 5,
            ElapsedMs:              sw.ElapsedMilliseconds);
    }

    private void AddSupplyChainLedgerForDemo(Subsidy demoSubsidy, Guid actorId)
    {
        var batchId = $"subsidy-{demoSubsidy.Id:N}";
        var prev = "0".PadLeft(64, '0');

        var entries = new (string EventType, object Payload, DateTime At)[]
        {
            ("registered", new {
                subsidy_id = demoSubsidy.Id,
                amount_kzt = demoSubsidy.Amount,
                crop_type = demoSubsidy.CropType,
                farm_id = demoSubsidy.FarmId,
            }, Utc(-30)),
            ("tranche_released", new {
                tranche_order = 1,
                amount_kzt = 2_400_000m,
                unlock_condition = "registered",
            }, Utc(-30)),
            ("sowing_confirmed", new {
                source = "satellite-ndvi",
                ndvi = 0.32,
                threshold = 0.18,
            }, Utc(-23)),
            ("tranche_released", new {
                tranche_order = 2,
                amount_kzt = 2_400_000m,
                unlock_condition = "sowing_confirmed",
            }, Utc(-22)),
        };

        foreach (var (eventType, payload, at) in entries)
        {
            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var hashInput = $"{prev}|{batchId}|{eventType}|{payloadJson}|{at:O}";
            var entryHash = Sha256(hashInput);
            db.SupplyChainLedger.Add(new SupplyChainLedgerEntry
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                EventType = eventType,
                PayloadJson = payloadJson,
                ActorId = actorId,
                PrevHash = prev,
                EntryHash = entryHash,
                CreatedAt = at,
            });
            prev = entryHash;
        }
    }

    private void AddLedgerChain(Guid farmId, string type, int[] counts, Guid actorId)
    {
        string prev = "0".PadLeft(64, '0');
        var nowBase = DateTime.UtcNow.AddDays(-counts.Length);
        for (var i = 0; i < counts.Length; i++)
        {
            var hash = Sha256($"{farmId}|{type}|{counts[i]}|{i}|{prev}");
            db.LivestockLedger.Add(new LivestockLedger
            {
                Id = Guid.NewGuid(), FarmId = farmId, LivestockType = type,
                Count = counts[i], PrevHash = prev, EntryHash = hash,
                Source = "demo_seed", CreatedByUserId = actorId,
                CreatedAt = nowBase.AddDays(i),
            });
            prev = hash;
        }
    }

    private User MakeUser(string email, string fullName, Role role, string? region = null, long? chatId = null) =>
        new()
        {
            Id = Guid.NewGuid(), Email = email,
            PasswordHash = hasher.Hash("Demo123!"), FullName = fullName,
            Role = role, AssignedRegion = region, TelegramChatId = chatId,
            IsEmailVerified = true, IsActive = true,
            CreatedAt = Utc(-30), UpdatedAt = Utc(-30),
        };

    private Farm MakeFarm(string name, User owner, string region, string district, double lat, double lng,
        decimal area, string crop, decimal ndvi, int risk, string ownerIin, string bankBin, Guid? elevator = null)
        => new()
        {
            Id = Guid.NewGuid(), OwnerId = owner.Id, Name = name, Region = region, District = district,
            AreaHectares = area, Lat = lat, Lng = lng, CropType = crop, RiskScore = risk,
            NdviMean = ndvi, NdviUpdatedAt = Utc(-1), DeviceId = $"ARD-{Math.Abs(name.GetHashCode()) % 10000}",
            PolygonGeoJson = MakePolygon(lat, lng),
            OwnerIin = ownerIin, BankBin = bankBin, ElevatorContractId = elevator,
            CreatedAt = Utc(-60), UpdatedAt = Utc(-1),
        };

    private static PlantDiagnosis Diagnose(Guid farmId, Guid userId, string en, string ru,
        bool healthy, string severity, decimal confidence, string rec) =>
        new()
        {
            Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
            Disease = en, DiseaseRu = ru, Confidence = confidence, Severity = severity,
            IsHealthy = healthy, Recommendation = rec, ImageUrl = "",
            ModelVersion = "plant-cv-v2.1", CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 7)),
        };

    private static Subsidy Subsidy(Guid farmId, decimal amount, decimal area, SubsidyStatus status, string purpose) =>
        new()
        {
            Id = Guid.NewGuid(), FarmId = farmId, Amount = amount,
            DeclaredArea = area, Purpose = purpose, Status = status,
            SubmittedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(7, 60)),
        };

    private static SupplyChainBatch Batch(Farm farm, string crop, decimal init, decimal current, string status, string? reason)
    {
        var id = Guid.NewGuid();
        return new SupplyChainBatch
        {
            Id = id,
            BatchCode = $"{crop[..Math.Min(3, crop.Length)].ToUpperInvariant()}-{id.ToString("N")[..8].ToUpperInvariant()}-{DateTime.UtcNow:yyyyMMdd}",
            FarmId = farm.Id, CropType = crop, InitialWeightKg = init, CurrentWeightKg = current,
            HarvestDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-12)),
            CurrentHolderType = "farm", CurrentHolderId = farm.Id.ToString(),
            Status = status, FreezeReason = reason, CreatedAt = DateTime.UtcNow.AddDays(-10),
        };
    }

    private static NotificationSubscription Sub(Guid userId, string type, bool enabled) =>
        new() { Id = Guid.NewGuid(), UserId = userId, NotificationType = type, Enabled = enabled, CreatedAt = DateTime.UtcNow };

    private static DateTime Utc(int daysOffset) => DateTime.UtcNow.AddDays(daysOffset);

    private static string MakePolygon(double lat, double lng)
    {
        const double d = 0.01;
        return JsonSerializer.Serialize(new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { lng - d, lat - d }, new[] { lng + d, lat - d },
                    new[] { lng + d, lat + d }, new[] { lng - d, lat + d },
                    new[] { lng - d, lat - d },
                }
            }
        });
    }

    private static string Sha256(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
