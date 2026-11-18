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
