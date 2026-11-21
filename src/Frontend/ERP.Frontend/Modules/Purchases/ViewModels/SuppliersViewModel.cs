using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Modules.Purchases;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Purchases.ViewModels;

public sealed partial class SuppliersViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly PurchasesApi _api;

    public SuppliersViewModel(PurchasesApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("purchases.suppliers.write");
        _ = LoadCommand.ExecuteAsync(null);
    }

    public bool CanWrite { get; }

    public ObservableCollection<SupplierIdentificationTypeOption> IdentificationTypes { get; } = [];

    public ObservableCollection<SupplierItem> Suppliers { get; } = [];

    [ObservableProperty] public partial string? Search { get; set; }
    [ObservableProperty] public partial bool IncludeInactive { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageLabel)), NotifyPropertyChangedFor(nameof(CanGoPrevious)), NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int Page { get; set; } = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageLabel)), NotifyPropertyChangedFor(nameof(CanGoPrevious)), NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int TotalPages { get; set; } = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] public partial int TotalCount { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FormTitle))] public partial SupplierItem? Editing { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IdentificationType))]
    public partial SupplierIdentificationTypeOption? SelectedIdentificationType { get; set; }
    [ObservableProperty] public partial string IdentificationNumber { get; set; } = string.Empty;
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string? Email { get; set; }
    [ObservableProperty] public partial string? Phone { get; set; }
    [ObservableProperty] public partial string? Address { get; set; }
    [ObservableProperty] public partial string? IdentificationTypeError { get; set; }
    [ObservableProperty] public partial string? IdentificationNumberError { get; set; }
    [ObservableProperty] public partial string? NameError { get; set; }
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? SuccessMessage { get; set; }
    [ObservableProperty] public partial SupplierItem? PendingToggle { get; set; }

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";
    public bool CanGoPrevious => Page > 1;
    public bool CanGoNext => Page < Math.Max(TotalPages, 1);
    public bool IsEmpty => TotalCount == 0 && !IsBusy;
    public string FormTitle => Editing is null ? "Nuevo proveedor" : $"Editar {Editing.Name}";
    public string IdentificationType => SelectedIdentificationType?.SriCode ?? string.Empty;

    [RelayCommand]
    private Task LoadAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var pageTask = _api.ListSuppliersAsync(Page, PageSize, Search, IncludeInactive ? null : true);
            var typeTask = _api.GetSupplierIdentificationTypesAsync();
            await Task.WhenAll(pageTask, typeTask);
            var result = await pageTask;
            Suppliers.Clear();
            foreach (var supplier in result.Items) Suppliers.Add(supplier);
            IdentificationTypes.Clear();
            foreach (var type in await typeTask) IdentificationTypes.Add(type);
            if (SelectedIdentificationType is null && IdentificationTypes.Count > 0)
            {
                SelectedIdentificationType = IdentificationTypes[0];
            }
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    [RelayCommand] private Task SearchNowAsync() { Page = 1; return LoadAsync(); }
    [RelayCommand] private void ClearSearch() => Search = null;
    [RelayCommand] private Task NextPageAsync() { if (Page < TotalPages) { Page++; return LoadAsync(); } return Task.CompletedTask; }
    [RelayCommand] private Task PreviousPageAsync() { if (Page > 1) { Page--; return LoadAsync(); } return Task.CompletedTask; }

    [RelayCommand]
    private void Edit(SupplierItem supplier)
    {
        Editing = supplier;
        SelectedIdentificationType = IdentificationTypes.SingleOrDefault(option => option.SriCode == supplier.IdentificationType);
        IdentificationNumber = supplier.IdentificationNumber;
        Name = supplier.Name;
        Email = supplier.Email;
        Phone = supplier.Phone;
        Address = supplier.Address;
        ClearMessages();
    }

    [RelayCommand]
    private void NewSupplier()
    {
        Editing = null;
        SelectedIdentificationType = IdentificationTypes.FirstOrDefault();
        IdentificationNumber = Name = string.Empty;
        Email = Phone = Address = null;
        ClearMessages();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ClearMessages();
        await RunAsync(async () =>
        {
            try
            {
                var request = new SaveSupplier(IdentificationType, IdentificationNumber, Name, Email, Phone, Address);
                var saved = Editing is null
                    ? await _api.CreateSupplierAsync(request)
                    : await _api.UpdateSupplierAsync(Editing.Id, request);
                NewSupplier();
                SuccessMessage = $"Proveedor {saved.Name} guardado.";
            }
            catch (ApiException ex) when (ex.Problem?.Errors is { Count: > 0 } errors)
            {
                IdentificationTypeError = SupplierFieldError.For(errors, "IdentificationType");
                IdentificationNumberError = SupplierFieldError.For(errors, "IdentificationNumber");
                NameError = SupplierFieldError.For(errors, "Name");
                EmailError = SupplierFieldError.For(errors, "Email");
                throw;
            }
        });
        if (ErrorMessage is null) await LoadAsync();
    }

    [RelayCommand] private void AskToggle(SupplierItem supplier) => PendingToggle = supplier;
    [RelayCommand] private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (IsBusy || PendingToggle is not { } supplier) return;
        PendingToggle = null;
        await RunAsync(async () =>
        {
            var updated = await _api.SetSupplierStatusAsync(supplier.Id, !supplier.IsActive);
            SuccessMessage = $"Proveedor {updated.Name} {(updated.IsActive ? "activado" : "desactivado")}.";
        });
        await LoadAsync();
    }

    private void ClearMessages()
    {
        ErrorMessage = SuccessMessage = null;
        IdentificationTypeError = IdentificationNumberError = NameError = EmailError = null;
    }
}

internal static class SupplierFieldError
{
    public static string? For(Dictionary<string, string[]> errors, string field) =>
        errors.FirstOrDefault(error => string.Equals(error.Key, field, StringComparison.OrdinalIgnoreCase))
            .Value?.FirstOrDefault();
}
