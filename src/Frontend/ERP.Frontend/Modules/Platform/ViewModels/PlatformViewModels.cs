using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;
using ERP.Frontend.Shell;

namespace ERP.Frontend.Modules.Platform.ViewModels;

/// <summary>
/// Inicio de sesión de DESARROLLO: el usuario elige un correo semilla y la API lo autentica con la
/// cabecera X-Dev-User. Pendiente (PLAT-01): OIDC Authorization Code + PKCE con navegador del sistema.
/// </summary>
public sealed partial class LoginViewModel(ErpApiClient api, SessionState session, PageFactory pages, Uri apiBaseUrl) : ViewModelBase
{
    public string ApiBaseUrl { get; } = apiBaseUrl.ToString();

    public IReadOnlyList<string> DemoUsers { get; } =
        ["ana.admin@demo.local", "bruno.admin@demo.local", "carla.contadora@demo.local"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; } = "ana.admin@demo.local";

    private bool CanSignIn() => !string.IsNullOrWhiteSpace(Email) && Email.Contains('@', StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private Task SignInAsync() => RunAsync(async () =>
    {
        session.SignOut();
        session.SignIn(Email);
        var profile = await api.GetMyProfileAsync();
        session.SetProfile(profile.DisplayName);

        if (profile.Memberships.Count == 0)
        {
            ErrorMessage = "El usuario no pertenece a ninguna empresa activa.";
            return;
        }

        pages.Navigator.NavigateTo(pages.TenantSelection(profile));
    });
}

public sealed partial class TenantSelectionViewModel(ErpApiClient api, SessionState session, PageFactory pages, MyProfile profile) : ViewModelBase
{
    public string Greeting { get; } = $"Hola, {profile.DisplayName}";

    public ObservableCollection<Membership> Memberships { get; } = [.. profile.Memberships];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnterCommand))]
    public partial Membership? Selected { get; set; } = profile.Memberships.Count == 1 ? profile.Memberships[0] : null;

    private bool CanEnter() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanEnter))]
    private Task EnterAsync() => RunAsync(async () =>
    {
        session.SelectTenant(Selected!.TenantId, Selected.TenantName);
        var current = await api.GetCurrentTenantAsync();
        session.SetTenantDetails(current.MyPermissions, current.EnabledFeatures);
        pages.Navigator.NavigateTo(pages.Shell(current));
    });

    [RelayCommand]
    private void Back()
    {
        session.SignOut();
        pages.Navigator.NavigateTo(pages.Login());
    }
}

/// <summary>Panel inicial: datos legales (RUC, establecimientos, puntos de emisión) y sucursales de la empresa activa.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly ErpApiClient _api;

    public DashboardViewModel(ErpApiClient api, SessionState session)
    {
        _api = api;
        TenantName = session.TenantName ?? string.Empty;
        _ = LoadCommand.ExecuteAsync(null);
    }

    public string TenantName { get; }

    public ObservableCollection<Taxpayer> Taxpayers { get; } = [];

    [ObservableProperty]
    public partial int BranchCount { get; set; }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var taxpayers = await _api.GetTaxpayersAsync();
        var branches = await _api.GetBranchesAsync();
        Taxpayers.Clear();
        foreach (var taxpayer in taxpayers)
        {
            Taxpayers.Add(taxpayer);
        }

        BranchCount = branches.Count;
    });
}

public sealed partial class BranchesViewModel : ViewModelBase
{
    private readonly ErpApiClient _api;

    public BranchesViewModel(ErpApiClient api, SessionState session)
    {
        _api = api;
        CanManage = session.Can("platform.branches.manage");
        _ = LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Solo UX: la API rechaza la creación si el usuario no tiene el permiso.</summary>
    public bool CanManage { get; }

    public ObservableCollection<Branch> Branches { get; } = [];

    [ObservableProperty]
    public partial string NewCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewCity { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var branches = await _api.GetBranchesAsync();
        Branches.Clear();
        foreach (var branch in branches)
        {
            Branches.Add(branch);
        }
    });

    [RelayCommand]
    private Task CreateAsync() => RunAsync(async () =>
    {
        SuccessMessage = null;
        var created = await _api.CreateBranchAsync(new CreateBranch(NewCode, NewName, null, NewCity));
        Branches.Add(created);
        SuccessMessage = $"Sucursal {created.Code} creada.";
        NewCode = string.Empty;
        NewName = string.Empty;
        NewCity = null;
    });
}
