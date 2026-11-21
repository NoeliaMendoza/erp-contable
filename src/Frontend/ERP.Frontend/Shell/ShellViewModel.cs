using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Shell;

public sealed record MenuEntry(string Title, Func<ViewModelBase> Create);

/// <summary>
/// Shell con menú lateral. Muestra solo módulos habilitados para la empresa y acciones permitidas
/// (UX). La API vuelve a autorizar cada operación.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private readonly PageFactory _pages;
    private readonly IAuthenticationService _auth;
    private readonly ErpApiClient _api;

    public ShellViewModel(SessionState session, PageFactory pages, CurrentTenant tenant, IAuthenticationService auth, ErpApiClient api)
    {
        _session = session;
        _pages = pages;
        _auth = auth;
        _api = api;
        TenantName = tenant.Name;
        UserName = session.DisplayName ?? session.UserEmail ?? string.Empty;

        MenuItems.Add(new MenuEntry("Inicio", pages.Dashboard));
        if (session.Can("platform.branches.read"))
        {
            MenuItems.Add(new MenuEntry("Sucursales", pages.Branches));
        }

        if (session.EnabledFeatures.Contains("inventory"))
        {
            if (session.Can("inventory.products.read"))
            {
                MenuItems.Add(new MenuEntry("Productos y servicios", pages.InventoryProducts));
            }

            if (session.Can("inventory.categories.read"))
            {
                MenuItems.Add(new MenuEntry("Categorías", pages.InventoryCategories));
            }

            if (session.Can("inventory.units.read"))
            {
                MenuItems.Add(new MenuEntry("Unidades", pages.InventoryUnits));
            }
        }

        if (session.EnabledFeatures.Contains("accounting") && session.Can("accounting.accounts.read"))
        {
            MenuItems.Add(new MenuEntry("Plan de cuentas", pages.AccountingAccounts));
        }

        if (session.EnabledFeatures.Contains("purchases") && session.Can("purchases.suppliers.read"))
        {
            MenuItems.Add(new MenuEntry("Proveedores", pages.PurchaseSuppliers));
        }

        foreach (var module in FrontendModules.Pending.Where(m => session.EnabledFeatures.Contains(m.FeatureCode)))
        {
            MenuItems.Add(new MenuEntry(module.Title, () => pages.Placeholder(module)));
        }

        SelectedItem = MenuItems[0];
    }

    public string TenantName { get; }

    public string UserName { get; }

    public ObservableCollection<MenuEntry> MenuItems { get; } = [];

    [ObservableProperty]
    public partial MenuEntry? SelectedItem { get; set; }

    [ObservableProperty]
    public partial ViewModelBase? Content { get; set; }

    partial void OnSelectedItemChanged(MenuEntry? value)
    {
        if (value is not null)
        {
            Content = value.Create();
        }
    }

    /// <summary>Vuelve a la lista de empresas autorizadas sin cerrar la sesión.</summary>
    [RelayCommand]
    private Task ChangeTenantAsync() => RunAsync(async () =>
    {
        _session.ClearTenant();
        var profile = await _api.GetMyProfileAsync();
        _pages.Navigator.NavigateTo(_pages.TenantSelection(profile));
    });

    /// <summary>Cierre de sesión local: descarta los tokens y la empresa seleccionada.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        _session.SignOut();
        await _auth.SignOutAsync(CancellationToken.None);
        _pages.Navigator.NavigateTo(_pages.Login("Sesión cerrada."));
    }
}

/// <summary>Página provisional para módulos aún no implementados por su responsable.</summary>
public sealed class ModulePlaceholderViewModel(FrontendModule module) : ViewModelBase
{
    public string Title => module.Title;

    public string Owner => $"Responsable: {module.Owner} (asignación provisional)";

    public string Summary => module.Summary;
}
