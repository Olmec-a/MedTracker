using MedTracker.Application;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Services;
using MedTracker.Infrastructure;
using MedTracker.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Fail fast if JWT secret is weak
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be configured and at least 32 characters long. " +
        "Generate with: openssl rand -base64 48");

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/medtracker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Kestrel endpoints
builder.WebHost.ConfigureKestrel((context, options) =>
{
    // 5001 — HTTP/2 (gRPC без TLS) для Postman/grpcurl
    options.ListenAnyIP(5001, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);

    // 5003 — HTTP/1.1 для health-endpoint (Docker healthcheck через curl)
    options.ListenAnyIP(5003, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});

// Register layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// For user status cache in AuthInterceptor
builder.Services.AddMemoryCache();

// Persist DataProtection keys to /app/keys — survives container restarts
// В production это volume-mount, чтобы ключи переживали перезапуск
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))
    .SetApplicationName("MedTracker");

// Health checks: проверяем БД
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", HealthStatus.Unhealthy);

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

// Auto-migrate in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

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
app.MapHealthChecks("/health");       // проверка БД + сервиса
app.MapHealthChecks("/health/ready"); // готов принимать трафик
app.MapGet("/health/live", () => Results.Ok("alive")); // только liveness

app.MapGet("/", () => "MedTracker gRPC service is running. Use a gRPC client to communicate.");

Log.Information("MedTracker gRPC service started");
await app.RunAsync();