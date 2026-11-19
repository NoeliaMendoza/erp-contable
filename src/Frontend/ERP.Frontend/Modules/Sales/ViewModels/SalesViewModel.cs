using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Inventory.ViewModels;
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
        _ = LoadProductsAsync();
    }

    public bool CanConfirm { get; }
    public ObservableCollection<ProductItem> Products { get; } = [];

    [ObservableProperty]
    public partial ProductItem? SelectedProduct { get; set; }

    [ObservableProperty]
    public partial string CustomerName { get; set; } = "Consumidor final";

    [ObservableProperty]
    public partial string QuantityText { get; set; } = "1";

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial SaleResponse? LastSale { get; set; }

    [ObservableProperty]
    public partial bool CanSubmit { get; set; } = true;

    private async Task LoadProductsAsync()
    {
        await RunAsync(async () =>
        {
            var page = await _inventory.ListProductsAsync(1, 100, null, null, true);
            Products.Clear();
            foreach (var product in page.Items)
            {
                Products.Add(product);
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
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ErrorMessage = "Ingrese el nombre del cliente.";
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

        CanSubmit = false;
        var request = new ConfirmSaleRequest(CustomerName, null, null, _idempotencyKey,
            Guid.Parse("0195a000-0000-7000-8000-00000000f001"),
            [new SaleLineRequest(SelectedProduct.Id, quantity)]);
        var sale = await _api.ConfirmAsync(request);
        LastSale = sale;
        SuccessMessage = sale.Status == "Confirmed"
            ? $"Venta confirmada {sale.Number}: USD {sale.Total:0.00}. Estado SRI: pendiente de acuerdo."
            : $"Venta registrada en estado {sale.Status}.";
        _idempotencyKey = Guid.CreateVersion7().ToString();
        CanSubmit = true;
    });

    [RelayCommand]
    private void Reset()
    {
        SuccessMessage = null;
        LastSale = null;
        CustomerName = "Consumidor final";
        QuantityText = "1";
        SelectedProduct = null;
        _idempotencyKey = Guid.CreateVersion7().ToString();
        CanSubmit = true;
    }
}
