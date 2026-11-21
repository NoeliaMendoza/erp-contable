using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Sales.ViewModels;

public sealed partial class CustomersViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly SalesApi _api;

    public CustomersViewModel(SalesApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("sales.customers.write");
        FormType = IdentificationOption.All[0];
        _ = LoadAsync();
    }

    public bool CanWrite { get; }
    public IReadOnlyList<IdentificationOption> IdentificationTypes => IdentificationOption.All;
    public ObservableCollection<CustomerItem> Customers { get; } = [];

    [ObservableProperty] public partial string? Search { get; set; }
    [ObservableProperty] public partial bool IncludeInactive { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int TotalPages { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial CustomerItem? Editing { get; set; }
    [ObservableProperty] public partial IdentificationOption FormType { get; set; }
    [ObservableProperty] public partial string IdentificationNumber { get; set; } = string.Empty;
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string? Email { get; set; }
    [ObservableProperty] public partial string? Phone { get; set; }
    [ObservableProperty] public partial string? Address { get; set; }
    [ObservableProperty] public partial string? IdentificationNumberError { get; set; }
    [ObservableProperty] public partial string? NameError { get; set; }
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? PhoneError { get; set; }
    [ObservableProperty] public partial string? AddressError { get; set; }
    [ObservableProperty] public partial string? SuccessMessage { get; set; }
    [ObservableProperty] public partial CustomerItem? PendingToggle { get; set; }

    public string FormTitle => Editing is null ? "Nuevo cliente" : $"Editar {Editing.Name}";
    public bool IsEmpty => TotalCount == 0 && !IsBusy && ErrorMessage is null;
    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";
    public bool CanGoPrevious => !IsBusy && Page > 1;
    public bool CanGoNext => !IsBusy && Page < Math.Max(TotalPages, 1);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _api.ListCustomersAsync(Page, PageSize, Search, IncludeInactive ? null : true);
            Customers.Clear();
            foreach (var customer in result.Items)
            {
                Customers.Add(customer);
            }
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearErrors();
        if (string.IsNullOrWhiteSpace(IdentificationNumber))
        {
            IdentificationNumberError = "Ingrese el número de identificación.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Ingrese el nombre.";
            return;
        }

        await RunAsync(async () =>
        {
            var request = new SaveCustomer(FormType.Value, IdentificationNumber, Name, Email, Phone, Address);
            var saved = Editing is null
                ? await _api.CreateCustomerAsync(request)
                : await _api.UpdateCustomerAsync(Editing.Id, request);
            SuccessMessage = Editing is null
                ? $"Cliente {saved.Name} creado."
                : $"Cliente {saved.Name} actualizado.";
            NewCustomer();
            await LoadAsync();
        });
    }

    [RelayCommand]
    private void NewCustomer()
    {
        Editing = null;
        FormType = IdentificationOption.All[0];
        IdentificationNumber = string.Empty;
        Name = string.Empty;
        Email = null;
        Phone = null;
        Address = null;
        ClearErrors();
        SuccessMessage = null;
    }

    [RelayCommand]
    private void Edit(CustomerItem customer)
    {
        Editing = customer;
        FormType = IdentificationOption.All.SingleOrDefault(t => t.Value == customer.IdentificationType)
            ?? IdentificationOption.All[0];
        IdentificationNumber = customer.IdentificationNumber;
        Name = customer.Name;
        Email = customer.Email;
        Phone = customer.Phone;
        Address = customer.Address;
        ClearErrors();
    }

    [RelayCommand]
    private async Task ConfirmToggleAsync(CustomerItem? customer)
    {
        if (customer is null)
        {
            return;
        }
        PendingToggle = customer;
        await RunAsync(async () =>
        {
            await _api.SetCustomerStatusAsync(PendingToggle.Id, !PendingToggle.IsActive);
            PendingToggle = null;
            await LoadAsync();
        });
    }

    [RelayCommand]
    private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task PreviousAsync()
    {
        if (CanGoPrevious)
        {
            Page--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CanGoNext)
        {
            Page++;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SearchNowAsync()
    {
        Page = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        Search = null;
        Page = 1;
        await LoadAsync();
    }

    private void ClearErrors()
    {
        IdentificationNumberError = null;
        NameError = null;
        EmailError = null;
        PhoneError = null;
        AddressError = null;
    }
}
