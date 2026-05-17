using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MedTracker.Infrastructure.Data;

/// <summary>
/// Design-time фабрика DbContext для EF Core tools (миграции, scaffolding).
///
/// Используется ТОЛЬКО когда вы запускаете `dotnet ef ...` — не участвует
/// в рантайме приложения. В рантайме DbContext по-прежнему регистрируется
/// через AddInfrastructure() в обычном DI.
///
/// Зачем нужна:
/// По умолчанию dotnet ef строит DbContext через DI стартап-проекта (Program.cs),
/// что требует валидации ВСЕХ регистраций приложения. Если какая-то зависимость
/// (Redis, gRPC-сервис, email-отправитель) не резолвится в design-time —
/// миграции падают, даже если к самой схеме БД это отношения не имеет.
///
/// С фабрикой EF берёт DbContext отсюда напрямую, минуя DI приложения.
/// Миграции работают независимо от состояния остального DI.
///
/// Это официально рекомендуемый Microsoft паттерн, не workaround:
/// https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Ищем appsettings рядом со startup project (MedTracker.Grpc).
        // dotnet ef запускается из MedTracker.Grpc как working directory,
        // поэтому Directory.GetCurrentDirectory() обычно даёт нужный путь.
        var basePath = Directory.GetCurrentDirectory();

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Provide it via appsettings.json or env var ConnectionStrings__DefaultConnection.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            // MigrationsAssembly должен совпадать с тем, что в AddInfrastructure() —
            // иначе EF не найдёт миграции в Infrastructure/Migrations.
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(optionsBuilder.Options);
    }
}