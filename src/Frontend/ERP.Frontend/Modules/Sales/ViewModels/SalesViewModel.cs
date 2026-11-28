using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Inventory.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Sales.ViewModels;

public sealed partial class SalesViewModel : ViewModelBase
{
    private readonly SalesApi _api;
    private readonly InventoryApi _inventory;
    private string _idempotencyKey = Guid.CreateVersion7().ToString();

    public SalesViewModel(SalesApi api, InventoryApi inventory, SessionState session)
    {
        _api = api;
        _inventory = inventory;
        CanConfirm = session.Can("sales.invoices.issue");
        CanRead = session.Can("sales.invoices.read");
        _ = InitializeAsync();
    }

    public bool CanConfirm { get; }
    public bool CanRead { get; }
    public ObservableCollection<ProductItem> Products { get; } = [];
    public ObservableCollection<CustomerItem> Customers { get; } = [];
    public ObservableCollection<WarehouseItem> Warehouses { get; } = [];
    public ObservableCollection<SaleResponse> Sales { get; } = [];

    [ObservableProperty]
    public partial ProductItem? SelectedProduct { get; set; }

    [ObservableProperty] public partial CustomerItem? SelectedCustomer { get; set; }

    [ObservableProperty] public partial WarehouseItem? SelectedWarehouse { get; set; }

    [ObservableProperty]
    public partial string QuantityText { get; set; } = "1";

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial SaleResponse? LastSale { get; set; }

    [ObservableProperty]
    public partial bool CanSubmit { get; set; } = true;

    [ObservableProperty] public partial int SalesPage { get; set; } = 1;
    [ObservableProperty] public partial int SalesTotalPages { get; set; } = 1;

    public string SalesPageLabel => $"Página {SalesPage} de {Math.Max(SalesTotalPages, 1)}";
    public bool CanGoPrevious => !IsBusy && SalesPage > 1;
    public bool CanGoNext => !IsBusy && SalesPage < Math.Max(SalesTotalPages, 1);

    partial void OnSalesPageChanged(int value)
    {
        OnPropertyChanged(nameof(SalesPageLabel));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnSalesTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(SalesPageLabel));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            var page = await _inventory.ListProductsAsync(1, 100, null, null, true);
            Products.Clear();
            foreach (var product in page.Items)
            {
                Products.Add(product);
            }
            var customers = await _api.ListCustomersAsync(1, 100, null, true);
            Customers.Clear();
            foreach (var customer in customers.Items)
            {
                Customers.Add(customer);
            }
            var warehouses = await _inventory.ListWarehousesAsync();
            Warehouses.Clear();
            foreach (var warehouse in warehouses.Where(w => w.IsActive))
            {
                Warehouses.Add(warehouse);
            }
            SelectedWarehouse = Warehouses.FirstOrDefault(w => w.IsDefault) ?? Warehouses.FirstOrDefault();
            if (CanRead)
            {
                await LoadSalesCoreAsync();
            }
        });
    }

    [RelayCommand]
    private Task ConfirmAsync() => RunAsync(async () =>
    {
        if (!CanSubmit)
        {
            return;
        }
        if (SelectedCustomer is null)
        {
            ErrorMessage = "Seleccione un cliente registrado.";
            return;
        }
        if (SelectedProduct is null)
        {
            ErrorMessage = "Seleccione un producto o servicio.";
            return;
        }
        if (!decimal.TryParse(QuantityText, out var quantity) || quantity <= 0)
        {
            ErrorMessage = "Ingrese una cantidad válida mayor que cero.";
            return;
        }
        if (SelectedWarehouse is null)
        {
            ErrorMessage = "Seleccione una bodega activa.";
            return;
        }

        CanSubmit = false;
        try
        {
            var request = new ConfirmSaleRequest(SelectedCustomer.Id, null, _idempotencyKey,
                SelectedWarehouse.Id, [new SaleLineRequest(SelectedProduct.Id, quantity)]);
            var sale = await _api.ConfirmAsync(request);
            LastSale = sale;
            SuccessMessage = sale.Status == "Confirmed"
                ? $"Venta confirmada {sale.Number}: USD {sale.Total:0.00}. Estado SRI: pendiente de acuerdo."
                : $"Venta registrada en estado {sale.Status}.";
            _idempotencyKey = Guid.CreateVersion7().ToString();
            SalesPage = 1;
            if (CanRead)
            {
                await LoadSalesCoreAsync();
            }
        }
        finally
        {
            CanSubmit = true;
        }
    });

    [RelayCommand]
    private void Reset()
    {
        SuccessMessage = null;
        LastSale = null;
        SelectedCustomer = null;
        QuantityText = "1";
        SelectedProduct = null;
        SelectedWarehouse = Warehouses.FirstOrDefault(w => w.IsDefault) ?? Warehouses.FirstOrDefault();
        _idempotencyKey = Guid.CreateVersion7().ToString();
        CanSubmit = true;
    }

    [RelayCommand]
    private Task LoadSalesAsync() => RunAsync(LoadSalesCoreAsync);

    private async Task LoadSalesCoreAsync()
    {
        var result = await _api.ListSalesAsync(SalesPage, 20, null);
        Sales.Clear();
        foreach (var sale in result.Items)
        {
            Sales.Add(sale);
        }
        SalesTotalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));
    }

    [RelayCommand]
    private async Task PreviousSalesPageAsync()
    {
        if (CanGoPrevious)
        {
            SalesPage--;
            await LoadSalesAsync();
        }
    }

    [RelayCommand]
    private async Task NextSalesPageAsync()
    {
        if (CanGoNext)
        {
            SalesPage++;
            await LoadSalesAsync();
        }
    }
}
