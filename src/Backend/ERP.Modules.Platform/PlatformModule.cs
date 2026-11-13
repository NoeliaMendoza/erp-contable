using ERP.BuildingBlocks.Security;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Platform.Data;
using ERP.Modules.Platform.Services;
using ERP.Modules.Platform.Validators;
using FluentValidation;
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
        services.AddScoped<ITenantAccessResolver, TenantAccessResolver>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<PlatformService>();
        services.AddSingleton<IPermissionProvider, PlatformPermissionProvider>();
        services.AddValidatorsFromAssemblyContaining<CreateBranchRequestValidator>();
        return services;
    }
}
