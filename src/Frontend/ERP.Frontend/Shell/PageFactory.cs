using ERP.Frontend.Modules.Platform.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Shell;

/// <summary>Crea ViewModels con sus dependencias. Cada módulo agrega aquí sus páginas.</summary>
public sealed class PageFactory(ErpApiClient api, SessionState session, FrontendSettings settings)
{
    public INavigator Navigator { get; set; } = null!;

    public LoginViewModel Login() => new(api, session, this, settings.ApiBaseUrl);

    public TenantSelectionViewModel TenantSelection(MyProfile profile) => new(api, session, this, profile);

    public ShellViewModel Shell(CurrentTenant tenant) => new(session, this, tenant);

    public DashboardViewModel Dashboard() => new(api, session);

    public BranchesViewModel Branches() => new(api, session);

    public ModulePlaceholderViewModel Placeholder(FrontendModule module) => new(module);
}

/// <summary>Módulos funcionales conocidos por el escritorio (el código coincide con tenant_features en la API).</summary>
public sealed record FrontendModule(string FeatureCode, string Title, string Owner, string Summary);

public static class FrontendModules
{
    public static readonly IReadOnlyList<FrontendModule> Pending =
    [
        new("accounting", "Contabilidad", "Integrante 2", "Plan de cuentas, períodos, asientos, libro diario y mayor."),
        new("sales", "Ventas", "Integrante 3", "Clientes, documentos de venta, cuentas por cobrar y comprobantes SRI."),
        new("purchases", "Compras y tesorería", "Integrante 4", "Proveedores, compras, pagos, cobros, caja y bancos."),
        new("inventory", "Inventario", "Integrante 5", "Productos, servicios, bodegas, movimientos y kardex."),
        new("reporting", "Reportes", "Integrante 6", "Dashboard, reportes PDF/Excel y conciliaciones internas."),
    ];
}
