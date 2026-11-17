using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Inventory;

// Modelos del cliente para /api/v1/units y /api/v1/products (espejo de ERP.Modules.Inventory.Dtos).

public sealed record UnitItem(Guid Id, string Code, string Name, bool IsActive)
{
    public string Display => $"{Code} — {Name}";

    public string Status => IsActive ? "Activa" : "Inactiva";
}

public sealed record ProductItem(
    Guid Id, string Sku, string Name, string Kind, Guid UnitId, string UnitCode, string UnitName, decimal Price, bool IsActive)
{
    public string KindLabel => Kind == "Service" ? "Servicio" : "Producto";

    public string Status => IsActive ? "Activo" : "Inactivo";
}

public sealed record SaveUnit(string Code, string Name);

public sealed record SaveProduct(string Sku, string Name, string Kind, Guid UnitId, decimal Price);

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

    public Task<PagedResult<ProductItem>> ListProductsAsync(
        int page, int pageSize, string? search, string? kind, bool? isActive, CancellationToken ct = default) =>
        api.GetAsync<PagedResult<ProductItem>>($"api/v1/products?{Query(page, pageSize, search, isActive, kind)}", ct);

    public Task<ProductItem> CreateProductAsync(SaveProduct request, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Post, "api/v1/products", request, ct);

    public Task<ProductItem> UpdateProductAsync(Guid id, SaveProduct request, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Put, $"api/v1/products/{id}", request, ct);

    public Task<ProductItem> SetProductStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<ProductItem>(HttpMethod.Patch, $"api/v1/products/{id}/status", new ChangeStatus(isActive), ct);

    internal static string Query(int page, int pageSize, string? search, bool? isActive, string? kind)
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

        return string.Join('&', parts);
    }
}
