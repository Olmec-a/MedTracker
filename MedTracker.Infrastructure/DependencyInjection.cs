using System.Text;
using MedTracker.Application.Interfaces;
using MedTracker.Infrastructure.Data;
using MedTracker.Infrastructure.Repositories;
using MedTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Hangfire;
using Hangfire.PostgreSql;
using MedTracker.Infrastructure.Jobs;

namespace MedTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ── PostgreSQL + EF Core ──
        // DefaultConnection = transaction-mode пул (pgbouncer port 6432, db=medtracker).
        // Подходит для обычных EF-запросов (SELECT/INSERT/UPDATE).
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // ── Redis ──
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "Redis connection string is not configured. Set ConnectionStrings:Redis.");

        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 3;
        redisOptions.ConnectTimeout = 5000;

        var multiplexer = ConnectionMultiplexer.Connect(redisOptions);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = redisConnection;
            opts.InstanceName = "medtracker:";
        });

        services.AddHybridCache(opts =>
        {
            opts.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });

        services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(multiplexer, "medtracker:DataProtection-Keys")
            .SetApplicationName("MedTracker");

        // ── Repositories ──
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDiagnosisRepository, DiagnosisRepository>();
        services.AddScoped<IMedicationRepository, MedicationRepository>();
        services.AddScoped<ISupplementRepository, SupplementRepository>();
        services.AddScoped<ISideEffectRepository, SideEffectRepository>();
        services.AddScoped<IUserDiagnosisRepository, UserDiagnosisRepository>();
        services.AddScoped<IUserMedicationRepository, UserMedicationRepository>();
        services.AddScoped<IUserSupplementRepository, UserSupplementRepository>();
        services.AddScoped<IUserSideEffectLogRepository, UserSideEffectLogRepository>();
        services.AddScoped<IExternalMedicationRepository, ExternalMedicationRepository>();
        services.AddScoped<IMenstrualCycleRepository, MenstrualCycleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IImportRecordRepository, ImportRecordRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        // ── Auth helpers ──
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddScoped<IExcelImportService, ExcelImportService>();

        // ── Email + URL builder ──
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<AppUrlOptions>(configuration.GetSection("App"));
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));

        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddSingleton<IAppUrlBuilder, AppUrlBuilder>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // ── Redis-based abstractions ──
        services.AddSingleton<ICatalogVersionStore, CatalogVersionStore>();
        services.AddSingleton<ICatalogCacheInvalidator, CatalogCacheInvalidator>();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IUserStatusCache, UserStatusCache>();

        // ── Background jobs (Hangfire) ──
        // Client (IBackgroundJobClient, RecurringJob.AddOrUpdate) регистрируется ВСЕГДА.
        // Server (worker pool) и recurring jobs — условно (см. HangfireOptions).

        var hangfireOptions = new HangfireOptions();
        configuration.GetSection("Hangfire").Bind(hangfireOptions);
        services.Configure<HangfireOptions>(configuration.GetSection("Hangfire"));

        services.AddScoped<IOutboxJob, OutboxJob>();
        services.AddScoped<ICleanupService, CleanupService>();

        // Hangfire требует session-mode соединение (использует LISTEN/NOTIFY).
        // Через pgbouncer transaction-mode это не работает.
        // ConnectionStrings:Hangfire — отдельная строка, указывает на pgbouncer database
        // "medtracker_admin" с pool_mode=session. Fallback на DefaultConnection —
        // для локального запуска без pgbouncer.
        var hangfireConnection = configuration.GetConnectionString("Hangfire")
            ?? configuration.GetConnectionString("DefaultConnection")!;

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(hangfireConnection),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(5),
                    DistributedLockTimeout = TimeSpan.FromMinutes(1)
                }));

        if (hangfireOptions.RunServer)
        {
            services.AddHangfireServer(opts =>
            {
                opts.WorkerCount = hangfireOptions.WorkerCount ?? Environment.ProcessorCount * 2;
                opts.Queues = new[] { "default" };
                opts.ServerName = $"medtracker:{Environment.MachineName}:{Guid.NewGuid().ToString()[..8]}";
            });
        }

        if (hangfireOptions.RegisterRecurringJobs)
        {
            services.AddHostedService<RecurringJobsRegistrar>();
        }

        // ── JWT Authentication ──
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT secret is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }
}