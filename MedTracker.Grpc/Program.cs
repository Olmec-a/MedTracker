using MedTracker.Application;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Services;
using MedTracker.Infrastructure;
using MedTracker.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Fail fast if JWT secret is weak
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be configured and at least 32 characters long. " +
        "Generate with: openssl rand -base64 48");

// Serilog — stdout only. В K8s/Docker логи собираются из stdout/stderr контейнера,
// файловый sink бесполезен (per-pod PVC + ротация без агрегатора = потеря данных).
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Kestrel endpoints
builder.WebHost.ConfigureKestrel((context, options) =>
{
    // 5001 — HTTP/2 (gRPC без TLS) для Postman/grpcurl
    options.ListenAnyIP(5001, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);

    // 5003 — HTTP/1.1 для health-endpoint (Docker/K8s healthcheck через curl)
    options.ListenAnyIP(5003, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});

// Register layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// For user status cache in AuthInterceptor (per-pod, TTL 60s — приемлемая неконсистентность)
builder.Services.AddMemoryCache();

// Correlation ID для трассировки запросов через все слои.
// Реализация (HttpContextCorrelationIdAccessor) живёт в слое Grpc, потому что
// id приходит из gRPC-метаданных через HttpContext.
// AddInfrastructure() не может зарегистрировать её сам — нет reference на Grpc-слой.
//
// ВНИМАНИЕ: если ваш HttpContextCorrelationIdAccessor лежит в другом namespace
// (не `MedTracker.Grpc`) — поправьте using выше. Узнать точное namespace:
//   head -1 MedTracker.Grpc/CorrelationIdAccessor.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

// ── Redis (общая шина для rate limiting и DataProtection между всеми репликами) ──
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException(
        "Redis connection string is not configured. " +
        "Set ConnectionStrings:Redis (e.g. 'medtracker-redis:6379').");

// AbortOnConnectFail = false: сервис стартует, даже если Redis недоступен в этот момент;
// переподключение произойдёт автоматически. Защищает от race при старте подов.
var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.AbortOnConnectFail = false;
var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// DataProtection в Redis — ключи общие для всех реплик.
// Заменяет file-based persist (старая версия писала в /app/keys).
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "MedTracker:DataProtection-Keys")
    .SetApplicationName("MedTracker");

// Health checks: проверяем БД + Redis
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", HealthStatus.Unhealthy)
    .AddRedis(redisConnectionString, name: "redis", failureStatus: HealthStatus.Degraded);

// gRPC
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<RateLimitInterceptor>();
    options.Interceptors.Add<RequestSizeInterceptor>();
    options.Interceptors.Add<AuthInterceptor>();
    options.Interceptors.Add<ExceptionInterceptor>();
    options.MaxReceiveMessageSize = 50 * 1024 * 1024; // 50 MB for Excel uploads
});
builder.Services.AddGrpcReflection();

var app = builder.Build();

// ВАЖНО: auto-migrate УБРАН.
// В multi-pod деплое одновременный старт реплик создаёт race condition на миграциях.
// Миграции запускаются отдельным шагом:
//   - локально: `dotnet ef database update` перед `docker compose up`
//   - в K8s: отдельный Job или initContainer, отрабатывающий ДО Deployment

// Map gRPC services
app.MapGrpcService<AuthGrpcService>();
app.MapGrpcService<UserProfileGrpcService>();
app.MapGrpcService<MedicationCatalogGrpcService>();
app.MapGrpcService<UserMedicationGrpcService>();
app.MapGrpcService<SideEffectLogGrpcService>();
app.MapGrpcService<ExternalMedicationGrpcService>();
app.MapGrpcService<MenstrualCycleGrpcService>();
app.MapGrpcService<AdminGrpcService>();

if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();

// Health check endpoints (plain HTTP/1.1)
app.MapHealthChecks("/health");       // проверка БД + Redis + сервиса
app.MapHealthChecks("/health/ready"); // готов принимать трафик
app.MapGet("/health/live", () => Results.Ok("alive")); // только liveness

app.MapGet("/", () => "MedTracker gRPC service is running. Use a gRPC client to communicate.");

Log.Information("MedTracker gRPC service started");
await app.RunAsync();