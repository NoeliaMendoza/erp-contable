using ERP.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Purchases;

/// <summary>
/// Módulo Compras y tesorería (Integrante 4). Estado: <b>pendiente</b> (registro y catálogo de permisos).
/// </summary>
public static class PurchasesModule
{
    public const string Module = "purchases";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Compras y tesorería", "Integrante 4");

    public static IServiceCollection AddPurchasesModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddControllers().AddApplicationPart(typeof(PurchasesModule).Assembly);
        services.AddSingleton<IPermissionProvider, PurchasesPermissionProvider>();
        return services;
    }
}

public static class PurchasesPermissions
{
    public const string SuppliersRead = "purchases.suppliers.read";
    public const string SuppliersManage = "purchases.suppliers.manage";
    public const string DocumentsRegister = "purchases.documents.register";
    public const string PaymentsRegister = "treasury.payments.register";
    public const string BanksManage = "treasury.banks.manage";
}

public sealed class PurchasesPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PurchasesPermissions.SuppliersRead, PurchasesModule.Module, "Consultar proveedores"),
        new(PurchasesPermissions.SuppliersManage, PurchasesModule.Module, "Administrar proveedores"),
        new(PurchasesPermissions.DocumentsRegister, PurchasesModule.Module, "Registrar compras"),
        new(PurchasesPermissions.PaymentsRegister, PurchasesModule.Module, "Registrar pagos y cobros"),
        new(PurchasesPermissions.BanksManage, PurchasesModule.Module, "Administrar caja y bancos"),
    ];
}
