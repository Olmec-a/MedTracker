using System.Text;
using MedTracker.Application.Interfaces;
using MedTracker.Infrastructure.Data;
using MedTracker.Infrastructure.Repositories;
using MedTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SendGrid;
using SendGrid.Extensions.DependencyInjection;

namespace MedTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL + EF Core
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // Repositories
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

        // Auth helpers
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddScoped<IExcelImportService, ExcelImportService>();

        // Email + URL builder
        services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
        services.Configure<AppUrlOptions>(configuration.GetSection("App"));
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));

        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddSingleton<IAppUrlBuilder, AppUrlBuilder>();

        // SendGrid SDK — регистрируем клиента, если задан API key.
        // На dev можно оставить пустым — IEmailSender бросит понятную ошибку,
        // но регистрация не упадёт.
        var sendGridApiKey = configuration["SendGrid:ApiKey"];
        if (!string.IsNullOrWhiteSpace(sendGridApiKey))
        {
            services.AddSendGrid(opt => opt.ApiKey = sendGridApiKey);
        }
        else
        {
            // Заглушка, чтобы DI не падал — реальные вызовы упадут с ясной ошибкой
            services.AddSingleton<ISendGridClient>(_ => new SendGridClient(""));
        }

        services.AddScoped<IEmailSender, SendGridEmailSender>();

        // Outbox background processor
        services.AddHostedService<OutboxProcessor>();

        // JWT Authentication
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