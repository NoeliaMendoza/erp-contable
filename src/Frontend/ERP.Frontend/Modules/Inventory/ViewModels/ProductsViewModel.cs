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

/// <summary>Opción de categoría para selectores: Id null = "sin categoría" (formulario) o "todas" (filtro).</summary>
public sealed record CategoryOption(Guid? Id, string Label);

/// <summary>
/// Catálogo de productos y servicios: listado con filtros, búsqueda (también por código de barras), paginación
/// y la ficha completa del artículo. No muestra existencias: el stock llegará con los movimientos de inventario.
/// </summary>
public sealed partial class ProductsViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private static readonly CategoryOption NoCategory = new(null, "Sin categoría");
    private static readonly CategoryOption AllCategories = new(null, "Todas las categorías");
    private readonly InventoryApi _api;

    public ProductsViewModel(InventoryApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("inventory.products.write");
        CanReadCategories = session.Can("inventory.categories.read");
        KindFilter = KindOption.Filters[0];
        FormKind = KindOption.FormKinds[0];
        CategoryFilter = AllCategories;
        FormCategory = NoCategory;
        CategoryFilters.Add(AllCategories);
        FormCategories.Add(NoCategory);
        _ = InitializeAsync();
    }

    public bool CanWrite { get; }

    public bool CanReadCategories { get; }

    public IReadOnlyList<KindOption> KindFilters => KindOption.Filters;

    public IReadOnlyList<KindOption> FormKinds => KindOption.FormKinds;

    public IReadOnlyList<VatOption> VatOptions => VatOption.All;

    public ObservableCollection<ProductItem> Products { get; } = [];

    /// <summary>Unidades ACTIVAS de la empresa activa para el selector.</summary>
    public ObservableCollection<UnitItem> Units { get; } = [];

    /// <summary>"Todas las categorías" + categorías activas.</summary>
    public ObservableCollection<CategoryOption> CategoryFilters { get; } = [];

    /// <summary>"Sin categoría" + categorías activas.</summary>
    public ObservableCollection<CategoryOption> FormCategories { get; } = [];

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial KindOption KindFilter { get; set; }

    [ObservableProperty]
    public partial CategoryOption CategoryFilter { get; set; }

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

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";

    public bool CanGoPrevious => Page > 1;

    public bool CanGoNext => Page < Math.Max(TotalPages, 1);

    public bool IsEmpty => TotalCount == 0 && !IsBusy;

    // ── Formulario ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial ProductItem? Editing { get; set; }

    [ObservableProperty]
    public partial string Sku { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProduct))]
    public partial KindOption FormKind { get; set; }

    [ObservableProperty]
    public partial UnitItem? FormUnit { get; set; }

    [ObservableProperty]
    public partial CategoryOption FormCategory { get; set; }

    /// <summary>Sin valor por defecto a propósito: el IVA siempre lo elige el usuario.</summary>
    [ObservableProperty]
    public partial VatOption? FormVat { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarginText))]
    public partial string PriceText { get; set; } = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarginText))]
    public partial string? CostText { get; set; }

    [ObservableProperty]
    public partial string? Barcode { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial bool TracksInventory { get; set; } = true;

    [ObservableProperty]
    public partial string? MinStockText { get; set; }

    [ObservableProperty]
    public partial string? SkuError { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? UnitError { get; set; }

    [ObservableProperty]
    public partial string? PriceError { get; set; }

    [ObservableProperty]
    public partial string? VatError { get; set; }

    [ObservableProperty]
    public partial string? CategoryError { get; set; }

    [ObservableProperty]
    public partial string? CostError { get; set; }

    [ObservableProperty]
    public partial string? BarcodeError { get; set; }

    [ObservableProperty]
    public partial string? DescriptionError { get; set; }

    [ObservableProperty]
    public partial string? MinStockError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial ProductItem? PendingToggle { get; set; }

    public string FormTitle => Editing is null ? "Nuevo producto o servicio" : $"Editar {Editing.Sku}";

    /// <summary>Un servicio nunca controla inventario: la casilla y el stock mínimo solo aplican a productos.</summary>
    public bool IsProduct => FormKind?.Value == "Product";

    /// <summary>Margen sobre el precio de venta, solo informativo: (precio − costo) / precio.</summary>
    public string MarginText =>
        PriceParser.TryParse(PriceText, out var price) && price > 0
        && PriceParser.TryParse(CostText, out var cost)
            ? $"Margen: {(price - cost) / price:P1}"
            : string.Empty;

    partial void OnKindFilterChanged(KindOption value) => _ = SearchNowAsync();

    partial void OnCategoryFilterChanged(CategoryOption value) => _ = SearchNowAsync();

    partial void OnFormKindChanged(KindOption value)
    {
        TracksInventory = value?.Value == "Product";
        if (!TracksInventory)
        {
            MinStockText = null;
        }
    }

    partial void OnTracksInventoryChanged(bool value)
    {
        if (!value)
        {
            MinStockText = null;
        }
    }

    private async Task InitializeAsync()
    {
        await LoadUnitsAsync();
        await LoadCategoriesAsync();
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

    private Task LoadCategoriesAsync()
    {
        if (!CanReadCategories)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var categories = await _api.ListCategoriesAsync(1, 100, null, isActive: true);
            foreach (var category in categories.Items)
            {
                var option = new CategoryOption(category.Id, category.Name);
                CategoryFilters.Add(option);
                FormCategories.Add(option);
            }
        });
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
            var result = await _api.ListProductsAsync(
                Page, PageSize, Search, KindFilter?.Value, IncludeInactive ? null : true, CategoryFilter?.Id);
            Products.Clear();
            foreach (var product in result.Items)
            {
                Products.Add(product);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    [RelayCommand]
    private Task SearchNowAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private void ClearSearch() => Search = null;

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
        FormCategory = product.CategoryId is null
            ? NoCategory
            : FormCategories.FirstOrDefault(c => c.Id == product.CategoryId)
              ?? new CategoryOption(product.CategoryId, $"{product.CategoryName} (inactiva)");
        FormVat = VatOptions.FirstOrDefault(v => v.Value == product.VatTreatment);
        PriceText = product.Price.ToString(CultureInfo.CurrentCulture);
        CostText = product.Cost?.ToString(CultureInfo.CurrentCulture);
        Barcode = product.Barcode;
        Description = product.Description;
        TracksInventory = product.TracksInventory;
        MinStockText = product.MinStock?.ToString(CultureInfo.CurrentCulture);
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
        FormCategory = NoCategory;
        FormVat = null;
        PriceText = "0";
        CostText = null;
        Barcode = null;
        Description = null;
        TracksInventory = true;
        MinStockText = null;
        ClearMessages();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearMessages();
        if (!TryBuildRequest(out var request))
        {
            return;
        }

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
                VatError = FieldError.For(errors, "VatTreatment");
                CategoryError = FieldError.For(errors, "CategoryId");
                CostError = FieldError.For(errors, "Cost");
                BarcodeError = FieldError.For(errors, "Barcode");
                DescriptionError = FieldError.For(errors, "Description");
                MinStockError = FieldError.For(errors, "MinStock") ?? FieldError.For(errors, "TracksInventory");
                throw;
            }
        });

        if (ErrorMessage is null)
        {
            await LoadAsync();
        }
    }

    /// <summary>Validación local mínima (formato de números y selecciones). Las reglas de negocio las aplica la API.</summary>
    private bool TryBuildRequest(out SaveProduct request)
    {
        request = null!;
        var valid = true;

        if (!PriceParser.TryParse(PriceText, out var price))
        {
            PriceError = "Ingrese un precio válido, por ejemplo 5,25.";
            valid = false;
        }

        decimal? cost = null;
        if (!string.IsNullOrWhiteSpace(CostText))
        {
            if (PriceParser.TryParse(CostText, out var parsedCost))
            {
                cost = parsedCost;
            }
            else
            {
                CostError = "Ingrese un costo válido, por ejemplo 3,20, o déjelo vacío.";
                valid = false;
            }
        }

        var tracks = IsProduct && TracksInventory;
        decimal? minStock = null;
        if (tracks && !string.IsNullOrWhiteSpace(MinStockText))
        {
            if (PriceParser.TryParse(MinStockText, out var parsedMin))
            {
                minStock = parsedMin;
            }
            else
            {
                MinStockError = "Ingrese una cantidad válida, por ejemplo 10, o déjelo vacío.";
                valid = false;
            }
        }

        if (FormUnit is null)
        {
            UnitError = "Seleccione una unidad de medida.";
            valid = false;
        }

        if (FormVat is null)
        {
            VatError = "Seleccione el tratamiento de IVA.";
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        request = new SaveProduct(
            Sku, Name, FormKind.Value!, FormUnit!.Id, price, FormVat!.Value, FormCategory?.Id,
            string.IsNullOrWhiteSpace(Barcode) ? null : Barcode,
            string.IsNullOrWhiteSpace(Description) ? null : Description,
            cost, tracks, minStock);
        return true;
    }

    [RelayCommand]
    private void AskToggle(ProductItem product) => PendingToggle = product;

    [RelayCommand]
    private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (IsBusy || PendingToggle is not { } product)
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
        VatError = null;
        CategoryError = null;
        CostError = null;
        BarcodeError = null;
        DescriptionError = null;
        MinStockError = null;
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
