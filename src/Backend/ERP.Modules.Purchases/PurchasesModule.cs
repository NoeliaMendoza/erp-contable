using ERP.BuildingBlocks.Security;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Purchases.Data;
using ERP.Modules.Purchases.Services;
using ERP.Modules.Purchases.Validators;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Purchases;

/// <summary>
/// Módulo Compras y tesorería (Integrante 4). Incluye el catálogo de proveedores.
/// </summary>
public static class PurchasesModule
{
    public const string Module = "purchases";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Compras y tesorería", "Integrante 4");

    public static IServiceCollection AddPurchasesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(PurchasesModule).Assembly);
        services.AddModuleDbContext<PurchasesDbContext>(configuration, PurchasesDbContext.Schema);
        services.AddScoped<SupplierService>();
        services.AddScoped<PurchaseService>();
        services.AddValidatorsFromAssemblyContaining<SaveSupplierRequestValidator>();
        services.AddSingleton<IPermissionProvider, PurchasesPermissionProvider>();
        return services;
    }
}

public static class PurchasesPermissions
{
    public const string SuppliersRead = "purchases.suppliers.read";
    public const string SuppliersWrite = "purchases.suppliers.write";
    public const string DocumentsRegister = "purchases.documents.register";
    public const string DocumentsRead = "purchases.documents.read";
    public const string PaymentsRegister = "treasury.payments.register";
    public const string BanksManage = "treasury.banks.manage";
}

public sealed class PurchasesPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PurchasesPermissions.SuppliersRead, PurchasesModule.Module, "Consultar proveedores"),
        new(PurchasesPermissions.SuppliersWrite, PurchasesModule.Module, "Crear, editar y desactivar proveedores"),
        new(PurchasesPermissions.DocumentsRegister, PurchasesModule.Module, "Registrar compras"),
        new(PurchasesPermissions.DocumentsRead, PurchasesModule.Module, "Consultar compras"),
        new(PurchasesPermissions.PaymentsRegister, PurchasesModule.Module, "Registrar pagos y cobros"),
        new(PurchasesPermissions.BanksManage, PurchasesModule.Module, "Administrar caja y bancos"),
    ];
}
