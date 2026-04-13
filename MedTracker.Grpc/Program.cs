using MedTracker.Application;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Services;
using MedTracker.Infrastructure;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/medtracker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Register layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// gRPC
builder.Services.AddGrpc(options =>
{
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

app.MapGet("/", () => "MedTracker gRPC service is running. Use a gRPC client to communicate.");

Log.Information("MedTracker gRPC service started");
await app.RunAsync();