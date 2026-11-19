using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Accounting.ViewModels;

/// <summary>Plan de cuentas: el servidor decide permisos, unicidad y validez de la jerarquía.</summary>
public sealed partial class AccountsViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly AccountingApi _api;
    private readonly List<AccountItem> _parentAccounts = [];

    public AccountsViewModel(AccountingApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("accounting.accounts.write");
        FormKind = Kinds[0];
        FormBalance = Balances[0];
        RebuildParents();
        _ = LoadCommand.ExecuteAsync(null);
    }

    public bool CanWrite { get; }

    public IReadOnlyList<AccountOption> Kinds => AccountOption.Kinds;

    public IReadOnlyList<AccountOption> Balances => AccountOption.Balances;

    public ObservableCollection<AccountItem> Accounts { get; } = [];

    public ObservableCollection<ParentAccountOption> Parents { get; } = [];

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int Page { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalCount { get; set; }

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)} · {TotalCount} cuenta(s)";

    public bool CanGoPrevious => !IsBusy && Page > 1;

    public bool CanGoNext => !IsBusy && Page < TotalPages;

    public bool IsEmpty => TotalCount == 0 && !IsBusy && ErrorMessage is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial AccountItem? Editing { get; set; }

    public string FormTitle => Editing is null ? "Nueva cuenta" : $"Editar {Editing.Code}";

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AccountOption? FormKind { get; set; }

    [ObservableProperty]
    public partial AccountOption? FormBalance { get; set; }

    [ObservableProperty]
    public partial ParentAccountOption? FormParent { get; set; }

    [ObservableProperty]
    public partial bool AllowsPosting { get; set; } = true;

    [ObservableProperty]
    public partial string? CodeError { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? KindError { get; set; }

    [ObservableProperty]
    public partial string? NormalBalanceError { get; set; }

    [ObservableProperty]
    public partial string? ParentIdError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial AccountItem? PendingToggle { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _api.ListAsync(Page, PageSize, Search, IncludeInactive ? null : true);
            if (result.Items.Count == 0 && Page > 1)
            {
                Page = Math.Max(1, result.TotalPages);
                result = await _api.ListAsync(Page, PageSize, Search, IncludeInactive ? null : true);
            }

            Accounts.Clear();
            foreach (var account in result.Items)
            {
                Accounts.Add(account);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            OnPropertyChanged(nameof(PageLabel));

            if (CanWrite)
            {
                // Cargar todas las páginas del selector, independientemente del filtro del listado.
                var parentId = FormParent?.Id;
                var all = new List<AccountItem>();
                var parentPage = 1;
                PagedResult<AccountItem> parents;
                do
                {
                    parents = await _api.ListAsync(parentPage++, 100, null, null);
                    all.AddRange(parents.Items);
                }
                while (parentPage <= parents.TotalPages);

                _parentAccounts.Clear();
                _parentAccounts.AddRange(all);
                RebuildParents(parentId);
            }
        });
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private Task SearchNowAsync()
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        if (!IsBusy)
        {
            Search = null;
        }
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        if (IsBusy || Page >= TotalPages)
        {
            return Task.CompletedTask;
        }

        Page++;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        if (IsBusy || Page <= 1)
        {
            return Task.CompletedTask;
        }

        Page--;
        return LoadAsync();
    }

    [RelayCommand]
    private void Edit(AccountItem account)
    {
        if (!CanWrite || IsBusy)
        {
            return;
        }

        Editing = account;
        Code = account.Code;
        Name = account.Name;
        FormKind = Kinds.Single(o => o.Value == account.Kind);
        FormBalance = Balances.Single(o => o.Value == account.NormalBalance);
        AllowsPosting = account.AllowsPosting;
        RebuildParents(account.ParentId);
        ClearMessages();
    }

    [RelayCommand]
    private void NewAccount()
    {
        if (IsBusy)
        {
            return;
        }

        ResetForm();
    }

    private void ResetForm()
    {
        Editing = null;
        Code = string.Empty;
        Name = string.Empty;
        FormKind = Kinds[0];
        FormBalance = Balances[0];
        AllowsPosting = true;
        RebuildParents();
        ClearMessages();
    }

    private void RebuildParents(Guid? selectedId = null)
    {
        // Excluir la cuenta y todos sus descendientes, incluyendo los inactivos.
        var excluded = new HashSet<Guid>();
        if (Editing is { } editing)
        {
            excluded.Add(editing.Id);
            bool changed;
            do
            {
                changed = false;
                foreach (var item in _parentAccounts.Where(a => a.ParentId is { } parent && excluded.Contains(parent)))
                {
                    changed |= excluded.Add(item.Id);
                }
            }
            while (changed);
        }

        Parents.Clear();
        Parents.Add(new(null, "Sin cuenta padre"));
        foreach (var account in _parentAccounts.Where(a => !excluded.Contains(a.Id)))
        {
            Parents.Add(new(account.Id, account.Display + (account.IsActive ? string.Empty : " (inactiva)")));
        }

        FormParent = Parents.FirstOrDefault(p => p.Id == selectedId) ?? Parents[0];
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanWrite || IsBusy)
        {
            return;
        }

        ClearMessages();
        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length > 40
            || !Code.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            CodeError = "Ingrese un código de hasta 40 letras, números, puntos o guiones.";
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 160)
        {
            NameError = "Ingrese un nombre de hasta 160 caracteres.";
        }

        KindError = FormKind is null ? "Seleccione el tipo." : null;
        NormalBalanceError = FormBalance is null ? "Seleccione la naturaleza." : null;
        if (CodeError is not null || NameError is not null || KindError is not null || NormalBalanceError is not null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            try
            {
                var request = new SaveAccount(Code, Name, FormKind!.Value, FormBalance!.Value, FormParent?.Id, AllowsPosting);
                var saved = Editing is null
                    ? await _api.CreateAsync(request)
                    : await _api.UpdateAsync(Editing.Id, request);
                ResetForm();
                SuccessMessage = $"Cuenta {saved.Code} guardada.";
            }
            catch (ApiException ex)
            {
                if (ex.Problem?.Errors is { } errors)
                {
                    CodeError = FieldError(errors, "Code");
                    NameError = FieldError(errors, "Name");
                    KindError = FieldError(errors, "Kind");
                    NormalBalanceError = FieldError(errors, "NormalBalance");
                    ParentIdError = FieldError(errors, "ParentId");
                }

                throw;
            }
        });

        if (ErrorMessage is null)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void AskToggle(AccountItem account)
    {
        if (CanWrite && !IsBusy)
        {
            PendingToggle = account;
        }
    }

    [RelayCommand]
    private void CancelToggle()
    {
        if (!IsBusy)
        {
            PendingToggle = null;
        }
    }

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (!CanWrite || IsBusy || PendingToggle is not { } account)
        {
            return;
        }

        ClearMessages();
        await RunAsync(async () =>
        {
            var updated = await _api.SetStatusAsync(account.Id, !account.IsActive);
            PendingToggle = null;
            SuccessMessage = $"Cuenta {updated.Code} {(updated.IsActive ? "activada" : "desactivada")}.";
        });

        // Conservar el error si la API rechazó el cambio.
        if (ErrorMessage is null)
        {
            await LoadAsync();
        }
    }

    private static string? FieldError(Dictionary<string, string[]> errors, string field) =>
        errors.FirstOrDefault(e => string.Equals(e.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        CodeError = null;
        NameError = null;
        KindError = null;
        NormalBalanceError = null;
        ParentIdError = null;
    }
}
