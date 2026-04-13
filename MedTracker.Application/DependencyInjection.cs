using FluentValidation;
using MedTracker.Application.Interfaces;
using MedTracker.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MedTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<Validators.RegisterDtoValidator>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IMedicationCatalogService, MedicationCatalogService>();
        services.AddScoped<IUserMedicationService, UserMedicationService>();
        services.AddScoped<ISideEffectLogService, SideEffectLogService>();
        services.AddScoped<IExternalMedicationService, ExternalMedicationService>();
        services.AddScoped<IMenstrualCycleService, MenstrualCycleService>();

        return services;
    }
}