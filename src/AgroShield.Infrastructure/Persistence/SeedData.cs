using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgroShield.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        if (await db.Users.AnyAsync())
        {
            logger.LogInformation("Seed data already present, skipping.");
            return;
        }

        logger.LogInformation("Seeding demo data...");

        var rng = new Random(42);

        // ── 1. Users ──────────────────────────────────────────────────────
        var inspector = new User
        {
            Id = Guid.NewGuid(), Email = "inspector@demo.kz",
            PasswordHash = hasher.Hash("Demo123!"), FullName = "Айдар Инспектор",
            PhoneNumber = "+77011234567", Role = Role.Inspector,
            IsEmailVerified = true, IsActive = true,
            CreatedAt = Utc(-30), UpdatedAt = Utc(-30),
        };
        var farmer = new User
        {
            Id = Guid.NewGuid(), Email = "farmer@demo.kz",
            PasswordHash = hasher.Hash("Demo123!"), FullName = "Бекзат Фермер",
            PhoneNumber = "+77019876543", Role = Role.Farmer,
            IsEmailVerified = true, IsActive = true,
            CreatedAt = Utc(-30), UpdatedAt = Utc(-30),
        };
        var admin = new User
        {
            Id = Guid.NewGuid(), Email = "admin@demo.kz",
            PasswordHash = hasher.Hash("Demo123!"), FullName = "Администратор",
            PhoneNumber = "+77000000000", Role = Role.Admin,
            IsEmailVerified = true, IsActive = true,
            CreatedAt = Utc(-30), UpdatedAt = Utc(-30),
        };

        db.Users.AddRange(inspector, farmer, admin);
        await db.SaveChangesAsync();

        // ── 2. Farms ──────────────────────────────────────────────────────
        var farmDefs = BuildFarmDefinitions();
        var farms = farmDefs.Select((f, i) => new Farm
        {
            Id = Guid.NewGuid(),
            OwnerId = i == 0 ? farmer.Id : (rng.Next(2) == 0 ? farmer.Id : inspector.Id),
            Name = f.Name,
            Region = "Северо-Казахстанская область",
            District = f.District,
            AreaHectares = (decimal)(rng.Next(50, 2000) + rng.NextDouble()),
            Lat = f.Lat,
            Lng = f.Lng,
            CropType = f.CropType,
            DeviceId = $"ARD-{rng.Next(1000, 9999)}",
            PolygonGeoJson = MakePolygon(f.Lat, f.Lng),
            RiskScore = i == 0 ? 20 : rng.Next(10, 96),
            CreatedAt = Utc(-rng.Next(30, 180)),
            UpdatedAt = Utc(-rng.Next(1, 29)),
        }).ToList();

        db.Farms.AddRange(farms);
        await db.SaveChangesAsync();

        // ── 3. Sensors + SensorReadings ───────────────────────────────────
        var sensors  = new List<Sensor>();
        var readings = new List<SensorReading>();
        var sensorTypes = Enum.GetValues<SensorType>();

        foreach (var farm in farms)
        {
            for (int s = 0; s < rng.Next(2, 4); s++)
                sensors.Add(new Sensor
                {
                    Id = Guid.NewGuid(), FarmId = farm.Id,
                    Type = sensorTypes[rng.Next(sensorTypes.Length)],
                    SerialNumber = $"SN-{rng.Next(10000, 99999)}",
                    InstalledAt = Utc(-rng.Next(60, 365)),
                    IsActive = rng.Next(10) > 1,
                });

            for (int r = 0; r < rng.Next(30, 51); r++)
                readings.Add(new SensorReading
                {
                    FarmId = farm.Id, DeviceId = farm.DeviceId!,
                    Temp = (decimal)(15 + rng.NextDouble() * 10),
                    Humidity = (decimal)(40 + rng.NextDouble() * 30),
                    Light = rng.Next(200, 1001), Fire = rng.Next(100) < 2,
                    WaterLevel = rng.Next(200, 801),
                    RecordedAt = DateTime.UtcNow.AddHours(-rng.Next(0, 168)),
                });
        }

        db.Sensors.AddRange(sensors);
        await db.SaveChangesAsync();

        foreach (var batch in readings.Chunk(500))
        {
            db.SensorReadings.AddRange(batch);
            await db.SaveChangesAsync();
        }

        // ── 4. Subsidies (50) ─────────────────────────────────────────────
        var subsidies = new List<Subsidy>();
        for (int i = 0; i < 50; i++)
        {
            var farm = farms[rng.Next(farms.Count)];
            bool suspicious = i < 25;
            decimal declared = (decimal)(rng.Next(50, 500) + rng.NextDouble());
            subsidies.Add(new Subsidy
            {
                Id = Guid.NewGuid(), FarmId = farm.Id,
                Amount = (decimal)rng.Next(500_000, 5_000_000),
                DeclaredArea = declared,
                ActiveAreaFromNdvi = suspicious ? declared * (decimal)(0.3 + rng.NextDouble() * 0.3) : null,
                NdviMeanScore = suspicious ? (decimal)(0.3 + rng.NextDouble() * 0.4) : null,
                Purpose = SubsidyPurposes[rng.Next(SubsidyPurposes.Length)],
                Status = Enum.GetValues<SubsidyStatus>()[rng.Next(4)],
                SubmittedAt = Utc(-rng.Next(1, 90)),
                CheckedAt = rng.Next(2) == 0 ? Utc(-rng.Next(0, 30)) : null,
            });
        }
        db.Subsidies.AddRange(subsidies);
        await db.SaveChangesAsync();

        // ── 5. Anomalies (20) ─────────────────────────────────────────────
        var anomalies = new List<Anomaly>();
        for (int i = 0; i < 20; i++)
        {
            var farm = farms[rng.Next(farms.Count)];
            var status = Enum.GetValues<AnomalyStatus>()[rng.Next(4)];
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), FarmId = farm.Id,
                EntityType = Enum.GetValues<AnomalyType>()[rng.Next(4)],
                EntityId = farm.Id,
                RiskScore = rng.Next(40, 100),
                Reasons = AnomalyReasons[rng.Next(AnomalyReasons.Length)],
                Status = status,
                DetectedAt = Utc(-rng.Next(1, 60)),
                ResolvedAt = status == AnomalyStatus.Closed ? Utc(-rng.Next(0, 10)) : null,
                ResolvedByUserId = status == AnomalyStatus.Closed ? inspector.Id : null,
                ResolutionNotes = status == AnomalyStatus.Closed ? "Проверено инспектором." : null,
            });
        }
        db.Anomalies.AddRange(anomalies);
        await db.SaveChangesAsync();

        // ── 6. PlantDiagnoses (25) ────────────────────────────────────────
        var diagnoses = new List<PlantDiagnosis>();
        for (int i = 0; i < 25; i++)
        {
            var farm = farms[rng.Next(farms.Count)];
            var disease = Diseases[rng.Next(Diseases.Length)];
            bool healthy = disease.En == "Healthy";
            diagnoses.Add(new PlantDiagnosis
            {
                Id = Guid.NewGuid(), FarmId = farm.Id, UserId = farmer.Id,
                ImageUrl = $"https://storage.example.com/diagnoses/demo_{i + 1}.jpg",
                Disease = disease.En, DiseaseRu = disease.Ru,
                Confidence = (decimal)(0.7 + rng.NextDouble() * 0.29),
                Severity = healthy ? "none" : Severities[rng.Next(Severities.Length)],
                IsHealthy = healthy, Recommendation = disease.Rec,
                ModelVersion = "plant-cv-v2.1",
                CreatedAt = Utc(-rng.Next(1, 60)),
            });
        }
        db.PlantDiagnoses.AddRange(diagnoses);
        await db.SaveChangesAsync();

        // ── 7. Animals (20) ──────────────────────────────────────────────
        var animals = new List<Animal>();
        for (int i = 0; i < 20; i++)
        {
            var farm = farms[rng.Next(farms.Count)];
            animals.Add(new Animal
            {
                Id = Guid.NewGuid(), FarmId = farm.Id,
                RfidTag = $"RF{rng.Next(100000, 999999):D6}",
                Species = Species[rng.Next(Species.Length)],
                BirthDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-rng.Next(180, 1825))),
                Status = Enum.GetValues<AnimalStatus>()[rng.Next(3)],
                CreatedAt = Utc(-rng.Next(10, 180)),
            });
        }
        db.Animals.AddRange(animals);
        await db.SaveChangesAsync();

        // ── 8. SupplyChainNodes (30) ──────────────────────────────────────
        var nodes = new List<SupplyChainNode>();
        string prevHash = "0000000000000000";
        for (int i = 0; i < 30; i++)
        {
            var fromFarm = farms[rng.Next(farms.Count)];
            string hash = Sha256Short($"{fromFarm.Id}{i}{rng.Next()}");
            nodes.Add(new SupplyChainNode
            {
                Id = Guid.NewGuid(),
                FromEntityId = fromFarm.Id, FromEntityName = fromFarm.Name,
                ToEntityId = Guid.NewGuid(), ToEntityName = Buyers[rng.Next(Buyers.Length)],
                Product = fromFarm.CropType,
                Quantity = (decimal)(rng.Next(10, 500) + rng.NextDouble()),
                Unit = "тонна", TransactionHash = hash, PreviousHash = prevHash,
                TransactionDate = Utc(-rng.Next(1, 120)),
                IsSuspicious = rng.Next(10) < 2,
                CreatedAt = Utc(-rng.Next(0, 5)),
            });
            prevHash = hash;
        }
        db.SupplyChainNodes.AddRange(nodes);
        await db.SaveChangesAsync();

        // ── 9. Recommendations (15) ───────────────────────────────────────
        var recommendations = new List<Recommendation>();
        for (int i = 0; i < 15; i++)
        {
            var farm = farms[rng.Next(farms.Count)];
            var rec = RecTemplates[rng.Next(RecTemplates.Length)];
            recommendations.Add(new Recommendation
            {
                Id = Guid.NewGuid(), FarmId = farm.Id,
                Priority = Enum.GetValues<RecommendationPriority>()[rng.Next(3)],
                Title = rec.Title, Description = rec.Description,
                Status = Enum.GetValues<RecommendationStatus>()[rng.Next(3)],
                CreatedAt = Utc(-rng.Next(1, 60)),
                CompletedAt = rng.Next(3) == 0 ? Utc(-rng.Next(0, 10)) : null,
            });
        }
        db.Recommendations.AddRange(recommendations);
        await db.SaveChangesAsync();

        // ── 10. Alerts (5) ────────────────────────────────────────────────
        db.Alerts.AddRange(
            new Alert { Id = Guid.NewGuid(), UserId = farmer.Id,    Type = AlertType.Anomaly,
                Title = "Обнаружена аномалия субсидии", FarmId = farms[0].Id,
                Message = "Площадь по NDVI значительно меньше заявленной.", IsRead = false, CreatedAt = Utc(-2) },
            new Alert { Id = Guid.NewGuid(), UserId = farmer.Id,    Type = AlertType.Fire,
                Title = "Тревога: возгорание", FarmId = farms[1].Id,
                Message = "Датчик зафиксировал признаки возгорания.", IsRead = false, CreatedAt = Utc(-1) },
            new Alert { Id = Guid.NewGuid(), UserId = inspector.Id, Type = AlertType.Anomaly,
                Title = "Новая аномалия требует проверки",
                Message = "Аномалия в цепочке поставок ожидает рассмотрения.", IsRead = false, CreatedAt = Utc(-3) },
            new Alert { Id = Guid.NewGuid(), UserId = farmer.Id,    Type = AlertType.LowWater,
                Title = "Низкий уровень воды", FarmId = farms[2].Id,
                Message = "Уровень воды ниже нормы.", IsRead = true, CreatedAt = Utc(-10) },
            new Alert { Id = Guid.NewGuid(), UserId = null,         Type = AlertType.Weather,
                Title = "Погодное предупреждение",
                Message = "Ожидается заморозок: -5°C ночью.", IsRead = false, CreatedAt = Utc(-1) }
        );
        await db.SaveChangesAsync();

        logger.LogInformation("Seed completed: {Farms} farms, {Readings} readings, {Subsidies} subsidies.",
            farms.Count, readings.Count, subsidies.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static DateTime Utc(int daysOffset) => DateTime.UtcNow.AddDays(daysOffset);

    private static string MakePolygon(double lat, double lng)
    {
        const double d = 0.01;
        return JsonSerializer.Serialize(new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[] {
                    new[] { lng - d, lat - d }, new[] { lng + d, lat - d },
                    new[] { lng + d, lat + d }, new[] { lng - d, lat + d },
                    new[] { lng - d, lat - d },
                }
            }
        });
    }

    private static string Sha256Short(string input) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input))).ToLower();

    // ── Static data ───────────────────────────────────────────────────────

    private static readonly string[] SubsidyPurposes =
    [
        "Субсидирование стоимости гербицидов", "Субсидии на приобретение семян",
        "Субсидии на горюче-смазочные материалы", "Субсидирование удобрений",
        "Субсидии на капельное орошение", "Компенсация затрат на уборку урожая",
    ];

    private static readonly string[][] AnomalyReasons =
    [
        ["Площадь по NDVI значительно меньше заявленной", "Индекс вегетации ниже нормы"],
        ["Нетипичные показания датчика температуры", "Возможная неисправность оборудования"],
        ["Подозрительная транзакция в цепочке поставок", "Несоответствие объёма поставки"],
        ["Низкий уровень влажности почвы", "Риск потери урожая"],
        ["Резкое изменение показателей датчиков", "Возможное возгорание в поле"],
    ];

    private static readonly string[] Severities = ["low", "medium", "high"];
    private static readonly string[] Species = ["Крупный рогатый скот", "Овца", "Лошадь", "Коза"];
    private static readonly string[] Buyers =
    [
        "Зерновой терминал Петропавл", "ТОО Агро-Трейд", "Мукомольный завод №1",
        "ТОО Север-Экспорт", "АО КазАгроФинанс", "ТОО Алтын Дала Трейд",
    ];

    private record DiseaseInfo(string En, string Ru, string Rec);
    private static readonly DiseaseInfo[] Diseases =
    [
        new("Healthy",                   "Здоровое растение",         "Продолжайте плановый уход."),
        new("Tomato___Late_blight",      "Фитофтороз томата",         "Обработайте фунгицидами на основе меди."),
        new("Potato___Early_blight",     "Альтернариоз картофеля",    "Применяйте фунгициды. Соблюдайте севооборот."),
        new("Wheat___Yellow_Rust",       "Жёлтая ржавчина пшеницы",  "Триазольные фунгициды. Мониторинг распространения."),
        new("Corn___Northern_Leaf_Blight","Северный ожог кукурузы",   "Устойчивые гибриды. Обработка при первых признаках."),
        new("Healthy",                   "Здоровое растение",         "Плановое наблюдение. Состояние удовлетворительное."),
    ];

    private record RecTemplate(string Title, string Description);
    private static readonly RecTemplate[] RecTemplates =
    [
        new("Провести химическую обработку",    "Обработайте поля пестицидами для борьбы с вредителями."),
        new("Проверить ирригационную систему",  "Уровень влажности почвы ниже оптимального. Проверьте форсунки."),
        new("Откалибровать датчики",            "Показания датчиков выходят за пределы нормы."),
        new("Заменить батарею устройства",      "Уровень заряда IoT-устройства критически низкий."),
        new("Внести азотные удобрения",         "Дефицит азота. Рекомендуется подкормка."),
        new("Провести пробный сбор урожая",     "Оцените готовность урожая перед массовой уборкой."),
        new("Обновить прошивку устройства",     "Доступна новая версия прошивки для Arduino."),
    ];

    private record FarmDef(string Name, string District, double Lat, double Lng, string CropType);

    private static FarmDef[] BuildFarmDefinitions() =>
    [
        new("Колос-Агро",       "Кызылжарский",        54.87, 69.17, "wheat"),
        new("Нива",             "Аккайынский",          54.12, 70.45, "barley"),
        new("Жер-Ана",          "Айыртауский",          54.55, 68.90, "wheat"),
        new("Береке",           "Мамлютский",           54.63, 68.45, "oilseed_rape"),
        new("Дастан",           "Тайыншинский",         53.85, 69.75, "wheat"),
        new("Алтын Дала",       "Г. Мусрепова",         54.20, 71.10, "sunflower"),
        new("Достык",           "Есильский",            54.00, 66.75, "flax"),
        new("Жуалы",            "Жамбылский",           53.70, 70.20, "barley"),
        new("Казахстан",        "Кызылжарский",         55.10, 69.50, "wheat"),
        new("Кызылжар",         "Кызылжарский",         54.75, 69.80, "potato"),
        new("Северное",         "Тимирязевский",        55.30, 68.20, "wheat"),
        new("Привольное",       "Магжана Жумабаева",    54.40, 72.00, "oilseed_rape"),
        new("Октябрьское",      "Аккайынский",          54.05, 70.90, "wheat"),
        new("Петропавловское",  "Кызылжарский",         54.90, 69.35, "barley"),
        new("Красный Яр",       "Тайыншинский",         53.60, 69.60, "sunflower"),
        new("Степное",          "Уалихановский",        53.50, 68.00, "wheat"),
        new("Урожай",           "Жамбылский",           53.80, 70.55, "buckwheat"),
        new("Заря",             "Мамлютский",           54.70, 67.90, "flax"),
        new("Восток",           "Есильский",            54.10, 66.50, "wheat"),
        new("Прогресс",         "Тимирязевский",        55.40, 68.60, "oilseed_rape"),
        new("Родина",           "Магжана Жумабаева",    54.30, 72.30, "barley"),
        new("Победа",           "Г. Мусрепова",         54.15, 71.50, "wheat"),
        new("Рассвет",          "Айыртауский",          54.60, 68.70, "sunflower"),
        new("Целинное",         "Аккайынский",          54.00, 70.65, "wheat"),
        new("Андреевское",      "Кызылжарский",         54.95, 69.00, "potato"),
        new("Ишим",             "Есильский",            54.25, 67.10, "barley"),
        new("Тобол",            "Тайыншинский",         53.90, 69.30, "wheat"),
        new("Есіл",             "Есильский",            54.05, 66.90, "flax"),
        new("Жаңа Жол",         "Уалихановский",        53.45, 67.80, "wheat"),
        new("Бірлік",           "Жамбылский",           53.75, 70.80, "sunflower"),
        new("Алтыбасар",        "Тимирязевский",        55.20, 68.40, "oilseed_rape"),
    ];
}
