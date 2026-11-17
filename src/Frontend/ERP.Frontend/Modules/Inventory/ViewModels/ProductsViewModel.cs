using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Inventory.ViewModels;

public sealed record KindOption(string? Value, string Label)
{
    public static readonly IReadOnlyList<KindOption> Filters =
        [new(null, "Todos"), new("Product", "Productos"), new("Service", "Servicios")];

    public static readonly IReadOnlyList<KindOption> FormKinds =
        [new("Product", "Producto"), new("Service", "Servicio")];
}

/// <summary>
/// Catálogo de productos y servicios: listado con filtro por tipo, búsqueda, paginación y formulario.
/// No muestra existencias: el stock llegará con los movimientos de inventario.
/// </summary>
public sealed partial class ProductsViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly InventoryApi _api;

    public ProductsViewModel(InventoryApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("inventory.products.write");
        KindFilter = KindOption.Filters[0];
        FormKind = KindOption.FormKinds[0];
        _ = InitializeAsync();
    }

    public bool CanWrite { get; }

    public IReadOnlyList<KindOption> KindFilters => KindOption.Filters;

    public IReadOnlyList<KindOption> FormKinds => KindOption.FormKinds;

    public ObservableCollection<ProductItem> Products { get; } = [];

    /// <summary>Unidades ACTIVAS de la empresa activa para el selector.</summary>
    public ObservableCollection<UnitItem> Units { get; } = [];

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial KindOption KindFilter { get; set; }

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    public partial int Page { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalCount { get; set; }

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";

    public bool IsEmpty => TotalCount == 0 && !IsBusy;

    // Formulario
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial ProductItem? Editing { get; set; }

    [ObservableProperty]
    public partial string Sku { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial KindOption FormKind { get; set; }

    [ObservableProperty]
    public partial UnitItem? FormUnit { get; set; }

    [ObservableProperty]
    public partial string PriceText { get; set; } = "0";

    [ObservableProperty]
    public partial string? SkuError { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? UnitError { get; set; }

    [ObservableProperty]
    public partial string? PriceError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial ProductItem? PendingToggle { get; set; }

    public string FormTitle => Editing is null ? "Nuevo producto o servicio" : $"Editar {Editing.Sku}";

    partial void OnKindFilterChanged(KindOption value) => _ = SearchNowAsync();

    private async Task InitializeAsync()
    {
        await LoadUnitsAsync();
        await LoadAsync();
    }

    private Task LoadUnitsAsync() => RunAsync(async () =>
    {
        var units = await _api.ListUnitsAsync(1, 100, null, isActive: true);
        Units.Clear();
        foreach (var unit in units.Items)
        {
            Units.Add(unit);
        }
    });

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var result = await _api.ListProductsAsync(Page, PageSize, Search, KindFilter?.Value, IncludeInactive ? null : true);
        Products.Clear();
        foreach (var product in result.Items)
        {
            Products.Add(product);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
        OnPropertyChanged(nameof(IsEmpty));
    });

    [RelayCommand]
    private Task SearchNowAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        if (Page >= TotalPages)
        {
            return Task.CompletedTask;
        }

        Page++;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        if (Page <= 1)
        {
            return Task.CompletedTask;
        }

        Page--;
        return LoadAsync();
    }

    [RelayCommand]
    private void Edit(ProductItem product)
    {
        Editing = product;
        Sku = product.Sku;
        Name = product.Name;
        FormKind = FormKinds.First(k => k.Value == product.Kind);
        FormUnit = Units.FirstOrDefault(u => u.Id == product.UnitId)
            ?? new UnitItem(product.UnitId, product.UnitCode, product.UnitName, IsActive: false);
        PriceText = product.Price.ToString(CultureInfo.CurrentCulture);
        ClearMessages();
    }

    [RelayCommand]
    private void NewProduct()
    {
        Editing = null;
        Sku = string.Empty;
        Name = string.Empty;
        FormKind = FormKinds[0];
        FormUnit = null;
        PriceText = "0";
        ClearMessages();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearMessages();
        if (!PriceParser.TryParse(PriceText, out var price))
        {
            PriceError = "Ingrese un precio válido, por ejemplo 5,25.";
            return;
        }

        if (FormUnit is null)
        {
            UnitError = "Seleccione una unidad de medida.";
            return;
        }

        var request = new SaveProduct(Sku, Name, FormKind.Value!, FormUnit.Id, price);
        await RunAsync(async () =>
        {
            try
            {
                var saved = Editing is null
                    ? await _api.CreateProductAsync(request)
                    : await _api.UpdateProductAsync(Editing.Id, request);
                NewProduct();
                SuccessMessage = $"{saved.KindLabel} {saved.Sku} guardado.";
            }
            catch (ApiException ex) when (ex.Problem?.Errors is { Count: > 0 } errors)
            {
                SkuError = FieldError.For(errors, "Sku");
                NameError = FieldError.For(errors, "Name");
                UnitError = FieldError.For(errors, "UnitId");
                PriceError = FieldError.For(errors, "Price");
                throw;
            }
        });

        if (ErrorMessage is null)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void AskToggle(ProductItem product) => PendingToggle = product;

    [RelayCommand]
    private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (PendingToggle is not { } product)
        {
            return;
        }

        PendingToggle = null;
        ClearMessages();
        await RunAsync(async () =>
        {
            var updated = await _api.SetProductStatusAsync(product.Id, !product.IsActive);
            SuccessMessage = $"{updated.KindLabel} {updated.Sku} {(updated.IsActive ? "activado" : "desactivado")}.";
        });
        await LoadAsync();
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        SkuError = null;
        NameError = null;
        UnitError = null;
        PriceError = null;
    }
}

internal static class PriceParser
{
    /// <summary>Acepta coma o punto decimal ("5,25" o "5.25"). Nunca usa float/double.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return normalized.Count(c => c == '.') <= 1
            && decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
