using ERP.BuildingBlocks.Security;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Platform.Data;
using ERP.Modules.Platform.Models;
using ERP.Modules.Platform.Services;
using ERP.Modules.Platform.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Platform;

/// <summary>
/// Módulo Plataforma (Integrante 1). Estructura MVC:
/// <c>Models/</c> entidades y reglas · <c>Controllers/</c> HTTP · <c>Services/</c> casos de uso ·
/// <c>Dtos/</c> contratos de entrada/salida · <c>Validators/</c> · <c>Data/</c> DbContext y migraciones.
/// </summary>
public static class PlatformModule
{
    public static readonly ModuleDescriptor Descriptor = new("platform", "Plataforma", "Integrante 1");

    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(PlatformModule).Assembly);
        services.AddModuleDbContext<PlatformDbContext>(configuration, PlatformDbContext.Schema);
        services.AddPlatformIdentity(configuration);
        services.AddScoped<ITenantAccessResolver, TenantAccessResolver>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<PlatformService>();
        services.AddSingleton<IPermissionProvider, PlatformPermissionProvider>();
        services.AddValidatorsFromAssemblyContaining<CreateBranchRequestValidator>();
        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity (solo usuarios, sin roles globales: los roles son por empresa en <c>platform</c>)
    /// sobre <see cref="AuthDbContext"/>. El emisor OpenIddict y los esquemas de autenticación se configuran en ERP.Api.
    /// </summary>
    private static void AddPlatformIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PersistenceRegistration.RuntimeConnectionName)
            ?? throw new InvalidOperationException(
                $"Falta ConnectionStrings:{PersistenceRegistration.RuntimeConnectionName}. Configúrela con User Secrets o variables de entorno (ver README).");

        services.AddDbContext<AuthDbContext>(options => AuthDbContext.Configure(options, connectionString));

        services.AddIdentityCore<ErpIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager();
    }
}
