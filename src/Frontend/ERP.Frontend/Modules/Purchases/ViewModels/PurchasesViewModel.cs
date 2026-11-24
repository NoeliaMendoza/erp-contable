using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Purchases.ViewModels;

public sealed partial class PurchasesViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly PurchasesApi _purchases;
    private readonly InventoryApi _inventory;

    public PurchasesViewModel(PurchasesApi purchases, InventoryApi inventory, SessionState session)
    {
        _purchases = purchases;
        _inventory = inventory;
        CanRegister = session.Can("purchases.documents.register");
        CanRead = session.Can("purchases.documents.read");
        _ = InitializeAsync();
    }

    public bool CanRegister { get; }
    public bool CanRead { get; }
    public ObservableCollection<SupplierItem> Suppliers { get; } = [];
    public ObservableCollection<ProductItem> Products { get; } = [];
    public ObservableCollection<PurchaseDraftLine> Lines { get; } = [];
    public ObservableCollection<PurchaseItem> Purchases { get; } = [];

    [ObservableProperty] public partial SupplierItem? SelectedSupplier { get; set; }
    [ObservableProperty] public partial ProductItem? SelectedProduct { get; set; }
    [ObservableProperty] public partial PurchaseDraftLine? SelectedLine { get; set; }
    [ObservableProperty] public partial string QuantityText { get; set; } = "1";
    [ObservableProperty] public partial string UnitCostText { get; set; } = "0.00";
    [ObservableProperty] public partial string? Search { get; set; }
    [ObservableProperty] public partial string? SuccessMessage { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int TotalPages { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }

    public decimal DraftTotal => Lines.Sum(line => line.Quantity * line.UnitCost);
    public bool IsEmpty => TotalCount == 0 && !IsBusy;
    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";

    private async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            if (CanRegister)
            {
                var suppliersTask = _purchases.ListSuppliersAsync(1, 100, null, true);
                var productsTask = _inventory.ListProductsAsync(1, 100, null, "Product", true);
                await Task.WhenAll(suppliersTask, productsTask);
                Suppliers.Clear();
                foreach (var supplier in (await suppliersTask).Items) Suppliers.Add(supplier);
                Products.Clear();
                foreach (var product in (await productsTask).Items.Where(item => item.TracksInventory)) Products.Add(product);
            }
        });
        if (CanRead) await LoadAsync();
    }

    [RelayCommand]
    private void AddLine()
    {
        SuccessMessage = null;
        if (SelectedProduct is null)
        {
            ErrorMessage = "Seleccione un artículo.";
            return;
        }
        if (!decimal.TryParse(QuantityText, out var quantity) || quantity <= 0 || decimal.Round(quantity, 4) != quantity)
        {
            ErrorMessage = "Ingrese una cantidad positiva con máximo 4 decimales.";
            return;
        }
        if (!decimal.TryParse(UnitCostText, out var unitCost) || unitCost < 0 || decimal.Round(unitCost, 4) != unitCost)
        {
            ErrorMessage = "Ingrese un costo unitario válido con máximo 4 decimales.";
            return;
        }
        if (Lines.Any(line => line.Product.Id == SelectedProduct.Id))
        {
            ErrorMessage = "El artículo ya está agregado; cada artículo aparece una sola vez.";
            return;
        }

        Lines.Add(new PurchaseDraftLine(SelectedProduct, quantity, unitCost));
        OnPropertyChanged(nameof(DraftTotal));
        ErrorMessage = null;
    }

    [RelayCommand]
    private void RemoveLine(PurchaseDraftLine line)
    {
        Lines.Remove(line);
        OnPropertyChanged(nameof(DraftTotal));
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        if (SelectedSupplier is null)
        {
            ErrorMessage = "Seleccione un proveedor activo.";
            return;
        }
        if (Lines.Count == 0)
        {
            ErrorMessage = "Agregue al menos un artículo.";
            return;
        }

        await RunAsync(async () =>
        {
            var created = await _purchases.RegisterPurchaseAsync(new RegisterPurchaseRequest(SelectedSupplier.Id,
                DateOnly.FromDateTime(DateTime.Today), Lines.Select(line => new PurchaseLineRequest(
                    line.Product.Id, line.Quantity, line.UnitCost)).ToArray()));
            SuccessMessage = $"Borrador {created.Number} registrado por USD {created.TotalCost:0.00}.";
            Lines.Clear();
            SelectedSupplier = null;
            OnPropertyChanged(nameof(DraftTotal));
        });
        if (ErrorMessage is null && CanRead) await LoadAsync();
    }

    [RelayCommand]
    private Task LoadAsync()
    {
        if (IsBusy || !CanRead) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var result = await _purchases.ListPurchasesAsync(Page, PageSize, Search, null);
            Purchases.Clear();
            foreach (var purchase in result.Items) Purchases.Add(purchase);
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(PageLabel));
        });
    }

    [RelayCommand] private Task SearchNowAsync() { Page = 1; return LoadAsync(); }
}

public sealed record PurchaseDraftLine(ProductItem Product, decimal Quantity, decimal UnitCost)
{
    public decimal LineCost => decimal.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);
}
