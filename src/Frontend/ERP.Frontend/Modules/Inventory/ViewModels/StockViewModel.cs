using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Mvvm;

namespace ERP.Frontend.Modules.Inventory.ViewModels;

/// <summary>
/// Existencias por bodega y kardex del artículo elegido. Solo lectura: el stock lo mueven Compras y Ventas,
/// nunca se digita (ACUERDO-01).
/// </summary>
public sealed partial class StockViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private const int KardexPageSize = 50;
    private readonly InventoryApi _api;
    private bool _loadingWarehouses;

    public StockViewModel(InventoryApi api)
    {
        _api = api;
        _ = InitializeAsync();
    }

    public ObservableCollection<WarehouseItem> Warehouses { get; } = [];

    public ObservableCollection<StockItem> Items { get; } = [];

    public ObservableCollection<KardexEntry> KardexEntries { get; } = [];

    [ObservableProperty]
    public partial WarehouseItem? SelectedWarehouse { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial int Page { get; set; } = 1;

    [ObservableProperty]
    public partial int TotalPages { get; set; } = 1;

    partial void OnPageChanged(int value) => NotifyPaging();

    partial void OnTotalPagesChanged(int value) => NotifyPaging();

    private void NotifyPaging()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string TotalValueLabel { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKardex))]
    public partial Kardex? SelectedKardex { get; set; }

    public bool HasKardex => SelectedKardex is not null;

    public bool CanGoPrevious => Page > 1;

    public bool CanGoNext => Page < Math.Max(TotalPages, 1);

    public bool IsEmpty => TotalCount == 0 && !IsBusy;

    partial void OnSelectedWarehouseChanged(WarehouseItem? value)
    {
        if (_loadingWarehouses || value is null)
        {
            return;
        }

        SelectedKardex = null;
        KardexEntries.Clear();
        Page = 1;
        _ = LoadAsync();
    }

    private async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            _loadingWarehouses = true;
            try
            {
                var warehouses = await _api.ListWarehousesAsync();
                Warehouses.Clear();
                foreach (var warehouse in warehouses)
                {
                    Warehouses.Add(warehouse);
                }

                SelectedWarehouse = Warehouses.FirstOrDefault(w => w.IsDefault) ?? Warehouses.FirstOrDefault();
            }
            finally
            {
                _loadingWarehouses = false;
            }
        });

        await LoadAsync();
    }

    [RelayCommand]
    private Task LoadAsync()
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var report = await _api.GetStockAsync(SelectedWarehouse?.Id, Page, PageSize, Search);
            Items.Clear();
            foreach (var item in report.Items.Items)
            {
                Items.Add(item);
            }

            TotalCount = report.Items.TotalCount;
            TotalPages = report.Items.TotalPages;
            TotalValueLabel = $"Valor de esta página: USD {report.Items.Items.Sum(i => i.TotalValue):N2}";
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    [RelayCommand]
    private Task SearchNowAsync() => GoToPageAsync(1);

    [RelayCommand]
    private void ClearSearch() => Search = null;

    [RelayCommand]
    private Task NextPageAsync() => GoToPageAsync(Page + 1);

    [RelayCommand]
    private Task PreviousPageAsync() => GoToPageAsync(Page - 1);

    private Task GoToPageAsync(int page)
    {
        var target = Math.Clamp(page, 1, Math.Max(TotalPages, 1));
        if (target == Page && page != 1)
        {
            return Task.CompletedTask;
        }

        Page = target;
        return LoadAsync();
    }

    /// <summary>Muestra el kardex del artículo en la bodega elegida (los 50 movimientos más antiguos primero).</summary>
    [RelayCommand]
    private Task ShowKardexAsync(StockItem item) => RunAsync(async () =>
    {
        var kardex = await _api.GetKardexAsync(item.ProductId, SelectedWarehouse?.Id, 1, KardexPageSize);
        KardexEntries.Clear();
        foreach (var entry in kardex.Entries.Items)
        {
            KardexEntries.Add(entry);
        }

        SelectedKardex = kardex;
    });

    [RelayCommand]
    private void CloseKardex()
    {
        SelectedKardex = null;
        KardexEntries.Clear();
    }
}
