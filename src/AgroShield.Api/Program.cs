using AgroShield.Api.Filters;
using AgroShield.Api.Hubs;
using AgroShield.Api.Middleware;
using AgroShield.Api.Services;
using AgroShield.Application.Auth;
using AgroShield.Application.Services;
using AgroShield.Application.Validators;
using AgroShield.Infrastructure.Auth;
using AgroShield.Infrastructure.BackgroundJobs;
using AgroShield.Infrastructure.ExternalServices;
using AgroShield.Infrastructure.Persistence;
using AgroShield.Infrastructure.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Service", "AgroShield.Api")
       .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;

// ── DB ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AgroShield.Infrastructure.Persistence.AnomalyEscalationInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention()
           .AddInterceptors(sp.GetRequiredService<AgroShield.Infrastructure.Persistence.AnomalyEscalationInterceptor>()));

// ── Core services ─────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IFarmService, FarmService>();
builder.Services.AddScoped<ISensorService, SensorService>();
builder.Services.AddScoped<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddScoped<IMLProxyService, MLProxyService>();
builder.Services.AddScoped<ITelegramLinkService, TelegramLinkService>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddScoped<IBatchFreezeService, BatchFreezeService>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<IClimateRiskService, ClimateRiskService>();
builder.Services.AddScoped<IVoiceEscalationService, VoiceEscalationService>();
builder.Services.AddScoped<IDemoSeedService, IdealDemoSeed>();

// ── Auth services ─────────────────────────────────────────────────────────
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IVerificationCodeService, VerificationCodeService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITelegramAuthService, TelegramAuthService>();

// ── Hangfire ──────────────────────────────────────────────────────────────
builder.Services.AddHangfire(cfg => cfg
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();
builder.Services.AddTransient<EvaluateAnomaliesJob>();
builder.Services.AddTransient<NdviUpdateJob>();
builder.Services.AddTransient<WeatherAlertJob>();
builder.Services.AddTransient<CleanupExpiredCodesJob>();
builder.Services.AddTransient<CleanupExpiredRefreshTokensJob>();
builder.Services.AddTransient<NdviDropDetectionJob>();
builder.Services.AddTransient<WeeklyReportJob>();
builder.Services.AddTransient<SupplyChainAnomalyJob>();
builder.Services.AddTransient<DailyTelegramDigestJob>();

// ── FluentValidation ──────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(typeof(CreateFarmDtoValidator).Assembly);
builder.Services.AddFluentValidationAutoValidation();

// ── ML HttpClients ────────────────────────────────────────────────────────
var internalApiKey = builder.Configuration["Security:InternalApiKey"] ?? "";
var mlSection = builder.Configuration.GetSection("MLServices");

builder.Services.AddTransient<InternalKeyHandler>();

void AddMlClient(string name, string? baseUrl, TimeSpan timeout)
{
    builder.Services.AddHttpClient(name, c =>
    {
        if (!string.IsNullOrEmpty(baseUrl)) c.BaseAddress = new Uri(baseUrl);
        c.Timeout = timeout;
    })
    .AddHttpMessageHandler<InternalKeyHandler>();
}

AddMlClient("PlantCv",            mlSection["PlantCv"],            TimeSpan.FromSeconds(30));
AddMlClient("YieldPredictor",     mlSection["YieldPredictor"],     TimeSpan.FromSeconds(30));
AddMlClient("AnomalyDetector",    mlSection["AnomalyDetector"],    TimeSpan.FromSeconds(30));
AddMlClient("AiAssistant",        mlSection["AiAssistant"],        TimeSpan.FromSeconds(120));
AddMlClient("SatelliteNdvi",      mlSection["SatelliteNdvi"],      TimeSpan.FromSeconds(60));
AddMlClient("TelegramBot",        mlSection["TelegramBot"],        TimeSpan.FromSeconds(10));
AddMlClient("LivestockMonitor",   mlSection["LivestockMonitor"],   TimeSpan.FromSeconds(60));
AddMlClient("FertilizerAdvisor",  mlSection["FertilizerAdvisor"],  TimeSpan.FromSeconds(15));
AddMlClient("SupplyChainTracker", mlSection["SupplyChainTracker"], TimeSpan.FromSeconds(15));

// self-call client for WeeklyReportJob
var selfPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.Services.AddHttpClient("SelfInternal", c =>
{
    c.BaseAddress = new Uri($"http://localhost:{selfPort}");
})
.AddHttpMessageHandler<InternalKeyHandler>();

// ── Auth ──────────────────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.MapInboundClaims = false;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidateAudience         = true,
            ValidAudience            = jwtSection["Audience"],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["Secret"]!)),
            ClockSkew                = TimeSpan.Zero,
            RoleClaimType            = "role",
        };
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

// ── Swagger ───────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "AgroShield API",
        Version     = "v1",
        Description = "Агромониторинг: фермы, датчики, субсидии, ML-диагностика",
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "JWT токен. Формат: Bearer {token}",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ── CORS ──────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

// ── JSON ──────────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

// ── Health checks ─────────────────────────────────────────────────────────
var hc = builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

void AddMlHealthCheck(string name, string? url)
{
    if (!string.IsNullOrEmpty(url))
        hc.AddUrlGroup(
            new Uri(url.TrimEnd('/') + "/health"),
            name: name,
            failureStatus: HealthStatus.Degraded,
            timeout: TimeSpan.FromSeconds(3));
}

AddMlHealthCheck("plant-cv",         mlSection["PlantCv"]);
AddMlHealthCheck("yield-predictor",  mlSection["YieldPredictor"]);
AddMlHealthCheck("anomaly-detector", mlSection["AnomalyDetector"]);
AddMlHealthCheck("satellite-ndvi",   mlSection["SatelliteNdvi"]);
AddMlHealthCheck("telegram-bot",     mlSection["TelegramBot"]);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ═════════════════════════════════════════════════════════════════════════
var app = builder.Build();

// ── Migrate + seed ────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

var envName  = app.Environment.EnvironmentName;
var seedFlag = Environment.GetEnvironmentVariable("SEED_DATA");
if (envName != "Production" || seedFlag == "true")
    await SeedData.InitializeAsync(app.Services);

// ── Middleware pipeline ───────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging();

var hangfireUser = builder.Configuration["Hangfire:DashboardUser"] ?? "admin";
var hangfirePass = builder.Configuration["Hangfire:DashboardPassword"] ?? "admin";
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireBasicAuthFilter(hangfireUser, hangfirePass)],
});

RecurringJob.AddOrUpdate<EvaluateAnomaliesJob>      ("evaluate-anomalies",    j => j.ExecuteAsync(), "*/10 * * * *");
RecurringJob.AddOrUpdate<NdviUpdateJob>             ("ndvi-update",           j => j.ExecuteAsync(), "0 2 * * 0");
RecurringJob.AddOrUpdate<NdviDropDetectionJob>      ("ndvi-drop-detection",   j => j.ExecuteAsync(), "0 8 * * *");
RecurringJob.AddOrUpdate<WeatherAlertJob>           ("weather-alert",         j => j.ExecuteAsync(), "0 6 * * *");
RecurringJob.AddOrUpdate<WeeklyReportJob>           ("weekly-reports",        j => j.ExecuteAsync(), "0 9 * * 1");
RecurringJob.AddOrUpdate<SupplyChainAnomalyJob>     ("supply-chain-anomaly",  j => j.ExecuteAsync(), "0 6 * * *");
RecurringJob.AddOrUpdate<DailyTelegramDigestJob>    ("daily-telegram-digest", j => j.ExecuteAsync(), "0 7 * * *", new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
RecurringJob.AddOrUpdate<CleanupExpiredCodesJob>    ("cleanup-codes",         j => j.ExecuteAsync(), Cron.Hourly);
RecurringJob.AddOrUpdate<CleanupExpiredRefreshTokensJob>("cleanup-refresh",   j => j.ExecuteAsync(), Cron.Daily);

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AgroShield API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseMiddleware<InternalUserContextMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SensorsHub>("/hubs/sensors");
app.MapHub<AlertsHub>("/hubs/alerts");

// ── Health endpoints ──────────────────────────────────────────────────────
static async Task WriteHealthJson(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var result = new
    {
        status     = report.Status.ToString(),
        components = report.Entries.Select(e => new
        {
            name     = e.Key,
            status   = e.Value.Status.ToString(),
            duration = e.Value.Duration.ToString(@"ss\.fff") + "s",
            error    = e.Value.Exception?.Message,
        }),
    };
    await ctx.Response.WriteAsJsonAsync(result);
}

app.MapHealthChecks("/health",       new HealthCheckOptions { ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate      = x => x.Tags.Contains("ready"),
    ResponseWriter = WriteHealthJson,
});
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();
