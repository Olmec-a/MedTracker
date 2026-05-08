using MedTracker.Application;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Services;
using MedTracker.Infrastructure;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
 
var builder = WebApplication.CreateBuilder(args);
 
// Validate JWT secret early
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("CHANGE_ME"))
    throw new InvalidOperationException(
        "JWT secret is missing or still has the placeholder value. " +
        "Generate with: openssl rand -base64 48");
 
// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/medtracker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
 
builder.Host.UseSerilog();
 
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.ListenAnyIP(5001, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    options.ListenAnyIP(5003, listen =>
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});
 
// Register layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
 
// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", HealthStatus.Unhealthy);
// (Опционально: добавь Redis health check, если хочется — см. README в этом блоке.)
 
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<RateLimitInterceptor>();
    options.Interceptors.Add<RequestSizeInterceptor>();
    options.Interceptors.Add<AuthInterceptor>();
    options.Interceptors.Add<ExceptionInterceptor>();
    options.MaxReceiveMessageSize = 50 * 1024 * 1024;
});
builder.Services.AddGrpcReflection();
 
var app = builder.Build();
 
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
 
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
 
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok("alive"));
app.MapGet("/", () => "MedTracker gRPC service is running.");
 
Log.Information("MedTracker gRPC service started");
await app.RunAsync();