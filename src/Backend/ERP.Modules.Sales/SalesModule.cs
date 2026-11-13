using ERP.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Sales;

/// <summary>
/// Módulo Ventas y adaptación SRI (Integrante 3). Estado: <b>pendiente</b> (registro y catálogo de permisos).
/// Contabiliza a través de <c>IAccountingPostingService</c> y mueve stock a través de <c>IInventoryMovementService</c>.
/// </summary>
public static class SalesModule
{
    public const string Module = "sales";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Ventas", "Integrante 3");

    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddControllers().AddApplicationPart(typeof(SalesModule).Assembly);
        services.AddSingleton<IPermissionProvider, SalesPermissionProvider>();
        return services;
    }
}

public static class SalesPermissions
{
    public const string CustomersRead = "sales.customers.read";
    public const string CustomersManage = "sales.customers.manage";
    public const string InvoicesRead = "sales.invoices.read";
    public const string InvoicesIssue = "sales.invoices.issue";
    public const string ReceivablesRead = "sales.receivables.read";
}

public sealed class SalesPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(SalesPermissions.CustomersRead, SalesModule.Module, "Consultar clientes"),
        new(SalesPermissions.CustomersManage, SalesModule.Module, "Administrar clientes"),
        new(SalesPermissions.InvoicesRead, SalesModule.Module, "Consultar documentos de venta"),
        new(SalesPermissions.InvoicesIssue, SalesModule.Module, "Emitir documentos de venta"),
        new(SalesPermissions.ReceivablesRead, SalesModule.Module, "Consultar cuentas por cobrar"),
    ];
}
