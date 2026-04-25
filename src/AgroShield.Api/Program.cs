using AgroShield.Api.Auth;
using AgroShield.Api.Hubs;
using AgroShield.Api.Middleware;
using AgroShield.Api.Services;
using AgroShield.Application.Auth;
using AgroShield.Application.Services;
using AgroShield.Infrastructure.ExternalServices;
using AgroShield.Infrastructure.Persistence;
using AgroShield.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFarmService, FarmService>();
builder.Services.AddScoped<ISensorService, SensorService>();
builder.Services.AddScoped<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddScoped<IClaimsTransformation, SupabaseClaimsTransformation>();
builder.Services.AddScoped<IMLProxyService, MLProxyService>();

var internalApiKey = builder.Configuration["Security:InternalApiKey"] ?? "";
var mlSection = builder.Configuration.GetSection("MLServices");

void AddMlClient(string name, string? baseUrl, TimeSpan timeout)
{
    builder.Services.AddHttpClient(name, c =>
    {
        if (!string.IsNullOrEmpty(baseUrl)) c.BaseAddress = new Uri(baseUrl);
        c.Timeout = timeout;
        if (!string.IsNullOrEmpty(internalApiKey))
            c.DefaultRequestHeaders.Add("X-Internal-Key", internalApiKey);
    });
}

AddMlClient("PlantCv",        mlSection["PlantCv"],        TimeSpan.FromSeconds(30));
AddMlClient("YieldPredictor", mlSection["YieldPredictor"], TimeSpan.FromSeconds(30));
AddMlClient("AnomalyDetector",mlSection["AnomalyDetector"],TimeSpan.FromSeconds(30));
AddMlClient("AiAssistant",    mlSection["AiAssistant"],    TimeSpan.FromSeconds(120));
AddMlClient("SatelliteNdvi",  mlSection["SatelliteNdvi"],  TimeSpan.FromSeconds(60));
AddMlClient("TelegramBot",    mlSection["TelegramBot"],    TimeSpan.FromSeconds(10));

var supabaseUrl = builder.Configuration["Supabase:Url"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabaseUrl}/auth/v1";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidAudience = "authenticated",
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var path = ctx.HttpContext.Request.Path;
                var token = ctx.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AgroShield API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token"
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
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

var envName = app.Environment.EnvironmentName;
var seedFlag = Environment.GetEnvironmentVariable("SEED_DATA");
if (envName != "Production" || seedFlag == "true")
{
    await SeedData.InitializeAsync(app.Services);
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SensorsHub>("/hubs/sensors");
app.MapHub<AlertsHub>("/hubs/alerts");
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                error = e.Value.Exception?.Message,
                description = e.Value.Description
            })
        };
        await ctx.Response.WriteAsJsonAsync(result);
    }
});

app.Run();
