using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;
using ERP.Frontend.Shell;

namespace ERP.Frontend.Modules.Platform.ViewModels;

/// <summary>
/// Inicio de sesión real (PLAT-01): abre el navegador del sistema (OIDC Authorization Code + PKCE) y, al volver,
/// carga el perfil y las empresas autorizadas. La aplicación nunca ve ni guarda la contraseña.
/// </summary>
public sealed partial class LoginViewModel(
    IAuthenticationService auth, ErpApiClient api, SessionState session, PageFactory pages, Uri apiBaseUrl, string? notice = null)
    : ViewModelBase
{
    private CancellationTokenSource? _signIn;

    public string ApiBaseUrl { get; } = apiBaseUrl.ToString();

    /// <summary>Aviso informativo (sesión expirada, inicio cancelado). Los errores van en <see cref="ViewModelBase.ErrorMessage"/>.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; } = notice;

    /// <summary>El navegador está abierto esperando que el usuario ingrese sus credenciales.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForBrowser { get; set; }

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        Notice = null;
        IsBusy = true;
        IsWaitingForBrowser = true;
        using var cts = new CancellationTokenSource();
        _signIn = cts;
        try
        {
            var result = await auth.SignInAsync(cts.Token);
            IsWaitingForBrowser = false;
            if (result.Status == SignInStatus.Cancelled)
            {
                Notice = result.Message ?? "Inicio de sesión cancelado.";
                return;
            }

            if (!result.Succeeded)
            {
                ErrorMessage = result.Message ?? "No se pudo iniciar sesión.";
                return;
            }

            var profile = await api.GetMyProfileAsync(cts.Token);
            session.SignOut();
            session.SignIn(profile.Email);
            session.SetProfile(profile.DisplayName);

            if (profile.Memberships.Count == 0)
            {
                ErrorMessage = "El usuario no pertenece a ninguna empresa activa.";
                await auth.SignOutAsync(CancellationToken.None);
                return;
            }

            pages.Navigator.NavigateTo(pages.TenantSelection(profile));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            await auth.SignOutAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Notice = "Inicio de sesión cancelado.";
        }
        finally
        {
            _signIn = null;
            IsWaitingForBrowser = false;
            IsBusy = false;
        }
    }

    /// <summary>Deja de esperar al navegador (el usuario cerró la pestaña o cambió de opinión).</summary>
    [RelayCommand]
    private void CancelSignIn() => _signIn?.Cancel();
}

public sealed partial class TenantSelectionViewModel(
    IAuthenticationService auth, ErpApiClient api, SessionState session, PageFactory pages, MyProfile profile) : ViewModelBase
{
    public string Greeting { get; } = $"Hola, {profile.DisplayName}";

    public ObservableCollection<Membership> Memberships { get; } = [.. profile.Memberships];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnterCommand))]
    public partial Membership? Selected { get; set; } = profile.Memberships.Count == 1 ? profile.Memberships[0] : null;

    private bool CanEnter() => Selected is not null;

    /// <summary>La API valida membresía y permisos de la empresa elegida (403 si no corresponde); aquí solo se selecciona.</summary>
    [RelayCommand(CanExecute = nameof(CanEnter))]
    private Task EnterAsync() => RunAsync(async () =>
    {
        session.SelectTenant(Selected!.TenantId, Selected.TenantName);
        try
        {
            var current = await api.GetCurrentTenantAsync();
            session.SetTenantDetails(current.MyPermissions, current.EnabledFeatures);
            pages.Navigator.NavigateTo(pages.Shell(current));
        }
        catch (ApiException)
        {
            session.ClearTenant();
            throw;
        }
    });

    [RelayCommand]
    private async Task BackAsync()
    {
        session.SignOut();
        await auth.SignOutAsync(CancellationToken.None);
        pages.Navigator.NavigateTo(pages.Login());
    }
}

/// <summary>Panel inicial: datos legales (RUC, establecimientos, puntos de emisión) y sucursales de la empresa activa.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly ErpApiClient _api;
    private readonly PageFactory _pages;

    public DashboardViewModel(ErpApiClient api, SessionState session, PageFactory pages)
    {
        _api = api;
        _pages = pages;
        TenantName = session.TenantName ?? string.Empty;
        _ = LoadCommand.ExecuteAsync(null);
    }

    public string TenantName { get; }

    public ObservableCollection<Taxpayer> Taxpayers { get; } = [];

    [ObservableProperty]
    public partial int BranchCount { get; set; }

    [RelayCommand]
    private void OpenUiGallery() => _pages.Navigator.NavigateTo(_pages.UiGallery());

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
    public partial string? NewCodeError { get; set; }

    [ObservableProperty]
    public partial string? NewNameError { get; set; }

    [ObservableProperty]
    public partial bool HasBranches { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsEmpty = false;
        SuccessMessage = null;
        await RunAsync(async () =>
        {
            var branches = await _api.GetBranchesAsync();
            Branches.Clear();
            foreach (var branch in branches)
            {
                Branches.Add(branch);
            }

            HasBranches = Branches.Count > 0;
            IsEmpty = !HasBranches;
        });

        if (ErrorMessage is not null)
        {
            IsEmpty = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        SuccessMessage = null;
        ErrorMessage = null;
        if (!ValidateForm())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var created = await _api.CreateBranchAsync(new CreateBranch(NewCode.Trim(), NewName.Trim(), null, NewCity?.Trim()));
            Branches.Add(created);
            SuccessMessage = $"Sucursal {created.Code} creada.";
            NewCode = string.Empty;
            NewName = string.Empty;
            NewCity = null;
            HasBranches = true;
            IsEmpty = false;
        });
    }

    partial void OnNewCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            NewCodeError = null;
        }
    }

    partial void OnNewNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            NewNameError = null;
        }
    }

    private bool ValidateForm()
    {
        NewCodeError = string.IsNullOrWhiteSpace(NewCode) ? "Escriba el código de la sucursal." : null;
        NewNameError = string.IsNullOrWhiteSpace(NewName) ? "Escriba el nombre de la sucursal." : null;
        return NewCodeError is null && NewNameError is null;
    }

}
