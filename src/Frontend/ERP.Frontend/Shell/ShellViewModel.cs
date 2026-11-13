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

    public ShellViewModel(SessionState session, PageFactory pages, CurrentTenant tenant)
    {
        _session = session;
        _pages = pages;
        TenantName = tenant.Name;
        UserName = session.DisplayName ?? session.UserEmail ?? string.Empty;

        MenuItems.Add(new MenuEntry("Inicio", pages.Dashboard));
        if (session.Can("platform.branches.read"))
        {
            MenuItems.Add(new MenuEntry("Sucursales", pages.Branches));
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

    [RelayCommand]
    private void ChangeTenant()
    {
        _session.ClearTenant();
        _pages.Navigator.NavigateTo(_pages.Login());
    }

    [RelayCommand]
    private void SignOut()
    {
        _session.SignOut();
        _pages.Navigator.NavigateTo(_pages.Login());
    }
}

/// <summary>Página provisional para módulos aún no implementados por su responsable.</summary>
public sealed class ModulePlaceholderViewModel(FrontendModule module) : ViewModelBase
{
    public string Title => module.Title;

    public string Owner => $"Responsable: {module.Owner} (asignación provisional)";

    public string Summary => module.Summary;
}
