using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Inventory.ViewModels;
using ERP.Frontend.Modules.Platform.ViewModels;
using ERP.Frontend.Modules.Ui.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Shell;

/// <summary>Crea ViewModels con sus dependencias. Cada módulo agrega aquí sus páginas.</summary>
public sealed class PageFactory(ErpApiClient api, SessionState session, FrontendSettings settings, IAuthenticationService auth)
{
    public INavigator Navigator { get; set; } = null!;

    public LoginViewModel Login(string? notice = null) => new(auth, api, session, this, settings.ApiBaseUrl, notice);

    public TenantSelectionViewModel TenantSelection(MyProfile profile) => new(auth, api, session, this, profile);

    public ShellViewModel Shell(CurrentTenant tenant) => new(session, this, tenant, auth, api);

    public DashboardViewModel Dashboard() => new(api, session, this);

    public ComponentGalleryViewModel UiGallery() => new();

    public BranchesViewModel Branches() => new(api, session);

    public ModulePlaceholderViewModel Placeholder(FrontendModule module) => new(module);

    // Inventario (Mendoza)
    public UnitsViewModel InventoryUnits() => new(new InventoryApi(api), session);

    public ProductsViewModel InventoryProducts() => new(new InventoryApi(api), session);

    public CategoriesViewModel InventoryCategories() => new(new InventoryApi(api), session);
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
        new("reporting", "Reportes", "Integrante 6", "Dashboard, reportes PDF/Excel y conciliaciones internas."),
    ];
}
