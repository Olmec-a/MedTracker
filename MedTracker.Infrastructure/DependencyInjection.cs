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
using SendGrid;
using SendGrid.Extensions.DependencyInjection;
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

        // Создаём multiplexer один раз и переиспользуем — для DI и для DataProtection.
        // AbortOnConnectFail=false означает, что если Redis ещё не поднялся — Connect() не бросит,
        // multiplexer перейдёт в "ConnectionFailed" state и будет переподключаться сам.
        var multiplexer = ConnectionMultiplexer.Connect(redisOptions);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        // IDistributedCache на Redis — нужен HybridCache как L2.
        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = redisConnection;
            opts.InstanceName = "medtracker:";
        });

        // HybridCache (L1 in-memory + L2 IDistributedCache).
        services.AddHybridCache(opts =>
        {
            opts.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });

        // ── DataProtection: ключи в Redis — общий keyring для всех реплик ──
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
        services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
        services.Configure<AppUrlOptions>(configuration.GetSection("App"));
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));

        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddSingleton<IAppUrlBuilder, AppUrlBuilder>();

        var sendGridApiKey = configuration["SendGrid:ApiKey"];
        if (!string.IsNullOrWhiteSpace(sendGridApiKey))
            services.AddSendGrid(opt => opt.ApiKey = sendGridApiKey);
        else
            services.AddSingleton<ISendGridClient>(_ => new SendGridClient(""));

        services.AddScoped<IEmailSender, SendGridEmailSender>();

        // ── Redis-based abstractions (catalog cache, rate limiter, user-status cache) ──
        services.AddSingleton<ICatalogVersionStore, CatalogVersionStore>();
        services.AddSingleton<ICatalogCacheInvalidator, CatalogCacheInvalidator>();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<IUserStatusCache, UserStatusCache>();
        
        // ── Background jobs (Hangfire) ──
        services.AddScoped<IOutboxJob, OutboxJob>();
        services.AddScoped<ICleanupService, CleanupService>();

        var pgConnection = configuration.GetConnectionString("DefaultConnection")!;
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(pgConnection),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(5),
                    DistributedLockTimeout = TimeSpan.FromMinutes(1)
                }));

        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = Environment.ProcessorCount * 2;
            opts.Queues = new[] { "default" };
            opts.ServerName = $"medtracker:{Environment.MachineName}:{Guid.NewGuid().ToString()[..8]}";
        });

        services.AddHostedService<RecurringJobsRegistrar>();

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