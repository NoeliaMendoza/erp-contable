using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Inventory;

// Modelos del cliente para /api/v1/units, /api/v1/categories y /api/v1/products (espejo de ERP.Modules.Inventory.Dtos).

public sealed record UnitItem(Guid Id, string Code, string Name, bool IsActive)
{
    public string Display => $"{Code} — {Name}";

    public string Status => IsActive ? "Activa" : "Inactiva";
}

public sealed record CategoryItem(Guid Id, string Name, bool IsActive)
{
    public string Status => IsActive ? "Activa" : "Inactiva";
}

public sealed record ProductItem(
    Guid Id,
    string Sku,
    string Name,
    string Kind,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    decimal Price,
    bool IsActive,
    string VatTreatment,
    Guid? CategoryId,
    string? CategoryName,
    string? Barcode,
    string? Description,
    decimal? Cost,
    bool TracksInventory,
    decimal? MinStock)
{
    public string KindLabel => Kind == "Service" ? "Servicio" : "Producto";

    public string VatLabel => VatOption.LabelFor(VatTreatment);

    public string CategoryLabel => CategoryName ?? "—";

    public string Status => IsActive ? "Activo" : "Inactivo";
}

public sealed record SaveUnit(string Code, string Name);

public sealed record SaveCategory(string Name);

public sealed record SaveProduct(
    string Sku,
    string Name,
    string Kind,
    Guid UnitId,
    decimal Price,
    string VatTreatment,
    Guid? CategoryId,
    string? Barcode,
    string? Description,
    decimal? Cost,
    bool TracksInventory,
    decimal? MinStock);

/// <summary>
/// Tratamiento de IVA. El porcentaje NO se muestra ni se guarda aquí: Ventas lo resuelve con la tarifa vigente
/// a la fecha de la factura (ver docs/modulos/inventario.md).
/// </summary>
public sealed record VatOption(string Value, string Label)
{
    public static readonly IReadOnlyList<VatOption> All =
    [
        new("Standard", "Gravado (tarifa general)"),
        new("ZeroRated", "Tarifa 0 %"),
        new("Exempt", "Exento de IVA"),
        new("NotSubject", "No objeto de IVA"),
    ];

    public static string LabelFor(string? value) =>
        All.FirstOrDefault(o => o.Value == value)?.Label ?? value ?? string.Empty;
}

public sealed record ChangeStatus(bool IsActive);

// ── Bodegas, existencias y kardex (INV-03) ─────────────────────────────────────────────────────

public sealed record WarehouseItem(Guid Id, string Code, string Name, bool IsDefault, bool IsActive)
{
    public string Display => IsDefault ? $"{Name} (principal)" : IsActive ? Name : $"{Name} (inactiva)";
}

public sealed record StockItem(
    Guid ProductId,
    string Sku,
    string Name,
    string UnitCode,
    bool IsActive,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal Available,
    decimal AverageCost,
    decimal TotalValue,
    decimal? MinStock,
    bool BelowMinimum)
{
    public string QuantityLabel => $"{QuantityOnHand:0.####} {UnitCode}";

    public string MinimumLabel => MinStock is { } min ? $"{min:0.####}" : "—";

    public string Alert => BelowMinimum ? "Bajo el mínimo" : string.Empty;
}

public sealed record StockReport(WarehouseItem Warehouse, PagedResult<StockItem> Items);

public sealed record KardexEntry(
    Guid MovementId,
    long Sequence,
    DateOnly MovementDate,
    DateTimeOffset RegisteredAtUtc,
    string Kind,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal? UnitCost,
    decimal LineCost,
    decimal? QuantityAfter,
    decimal? AverageCostAfter)
{
    public string Source => SourceModule switch
    {
        "purchases" => "Compra",
        "sales" => "Venta",
        _ => SourceModule,
    };

    public string InLabel => QuantityIn == 0 ? string.Empty : $"{QuantityIn:0.####}";

    public string OutLabel => QuantityOut == 0 ? string.Empty : $"{QuantityOut:0.####}";

    public string BalanceLabel => QuantityAfter is { } q ? $"{q:0.####}" : "—";

    public string AverageLabel => AverageCostAfter is { } c ? $"{c:0.0000}" : "—";
}

public sealed record Kardex(
    Guid ProductId,
    string Sku,
    string Name,
    string UnitCode,
    WarehouseItem Warehouse,
    decimal QuantityOnHand,
    decimal AverageCost,
    PagedResult<KardexEntry> Entries);

/// <summary>Cliente del catálogo de Inventario. Usa el cliente HTTP común (sesión, empresa y errores).</summary>
public sealed class InventoryApi(ErpApiClient api)
{
    public Task<PagedResult<UnitItem>> ListUnitsAsync(int page, int pageSize, string? search, bool? isActive, CancellationToken ct = default) =>
        api.GetAsync<PagedResult<UnitItem>>($"api/v1/units?{Query(page, pageSize, search, isActive, null)}", ct);

    public Task<UnitItem> CreateUnitAsync(SaveUnit request, CancellationToken ct = default) =>
        api.SendJsonAsync<UnitItem>(HttpMethod.Post, "api/v1/units", request, ct);

    public Task<UnitItem> UpdateUnitAsync(Guid id, SaveUnit request, CancellationToken ct = default) =>
        api.SendJsonAsync<UnitItem>(HttpMethod.Put, $"api/v1/units/{id}", request, ct);

    public Task<UnitItem> SetUnitStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<UnitItem>(HttpMethod.Patch, $"api/v1/units/{id}/status", new ChangeStatus(isActive), ct);

    public Task<PagedResult<CategoryItem>> ListCategoriesAsync(int page, int pageSize, string? search, bool? isActive, CancellationToken ct = default) =>
        api.GetAsync<PagedResult<CategoryItem>>($"api/v1/categories?{Query(page, pageSize, search, isActive, null)}", ct);

    public Task<CategoryItem> CreateCategoryAsync(SaveCategory request, CancellationToken ct = default) =>
        api.SendJsonAsync<CategoryItem>(HttpMethod.Post, "api/v1/categories", request, ct);

    public Task<CategoryItem> UpdateCategoryAsync(Guid id, SaveCategory request, CancellationToken ct = default) =>
        api.SendJsonAsync<CategoryItem>(HttpMethod.Put, $"api/v1/categories/{id}", request, ct);

    public Task<CategoryItem> SetCategoryStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<CategoryItem>(HttpMethod.Patch, $"api/v1/categories/{id}/status", new ChangeStatus(isActive), ct);

    public Task<PagedResult<ProductItem>> ListProductsAsync(
        int page, int pageSize, string? search, string? kind, bool? isActive, Guid? categoryId = null, CancellationToken ct = default) =>
        api.GetAsync<PagedResult<ProductItem>>($"api/v1/products?{Query(page, pageSize, search, isActive, kind, categoryId)}", ct);

    public Task<ProductItem> CreateProductAsync(SaveProduct request, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Post, "api/v1/products", request, ct);

    public Task<ProductItem> UpdateProductAsync(Guid id, SaveProduct request, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Put, $"api/v1/products/{id}", request, ct);

    public Task<ProductItem> SetProductStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Patch, $"api/v1/products/{id}/status", new ChangeStatus(isActive), ct);

    public Task<List<WarehouseItem>> ListWarehousesAsync(CancellationToken ct = default) =>
        api.GetAsync<List<WarehouseItem>>("api/v1/warehouses", ct);

    public Task<StockReport> GetStockAsync(Guid? warehouseId, int page, int pageSize, string? search, CancellationToken ct = default) =>
        api.GetAsync<StockReport>($"api/v1/stock?{Query(page, pageSize, search, true, null)}{WarehouseQuery(warehouseId)}", ct);

    public Task<Kardex> GetKardexAsync(Guid productId, Guid? warehouseId, int page, int pageSize, CancellationToken ct = default) =>
        api.GetAsync<Kardex>($"api/v1/products/{productId:D}/kardex?page={page}&pageSize={pageSize}{WarehouseQuery(warehouseId)}", ct);

    private static string WarehouseQuery(Guid? warehouseId) => warehouseId is { } id ? $"&warehouseId={id:D}" : string.Empty;

    internal static string Query(int page, int pageSize, string? search, bool? isActive, string? kind, Guid? categoryId = null)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add("search=" + Uri.EscapeDataString(search.Trim()));
        }

        if (isActive is not null)
        {
            parts.Add("isActive=" + (isActive.Value ? "true" : "false"));
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            parts.Add("kind=" + Uri.EscapeDataString(kind));
        }

        if (categoryId is not null)
        {
            parts.Add("categoryId=" + categoryId.Value.ToString("D"));
        }

        return string.Join('&', parts);
    }
}
