using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Purchases;

public sealed record SupplierIdentificationTypeOption(string SriCode, string Name);

public sealed record SupplierItem(
    Guid Id,
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive)
{
    public string Identification => $"{IdentificationType}: {IdentificationNumber}";

    public string Status => IsActive ? "Activo" : "Inactivo";
}

public sealed record SaveSupplier(
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address);

public sealed record ChangeSupplierStatus(bool IsActive);

public sealed record PurchaseLineRequest(Guid ProductId, decimal Quantity, decimal UnitCost);

public sealed record RegisterPurchaseRequest(Guid SupplierId, DateOnly? PurchaseDate,
    IReadOnlyList<PurchaseLineRequest> Lines);

public sealed record PurchaseLineItem(Guid Id, Guid ProductId, string Code, string Name, string UnitCode,
    decimal Quantity, decimal UnitCost, decimal LineCost);

public sealed record PurchaseItem(Guid Id, string Number, Guid SupplierId, string SupplierName,
    DateOnly PurchaseDate, Guid WarehouseId, string Status, decimal TotalCost,
    Guid? StockMovementId, Guid? JournalEntryId, IReadOnlyList<PurchaseLineItem> Lines)
{
    public bool IsDraft => Status == "Draft";

    public string StatusLabel => Status switch
    {
        "Draft" => "Borrador",
        "Confirmed" => "Confirmada",
        _ => Status,
    };
}

/// <summary>Cliente del módulo Compras sobre el cliente HTTP autenticado y tenant-aware compartido.</summary>
public sealed class PurchasesApi(ErpApiClient api)
{
    public Task<PurchaseItem> RegisterPurchaseAsync(RegisterPurchaseRequest request, CancellationToken ct = default) =>
        api.SendJsonAsync<PurchaseItem>(HttpMethod.Post, "api/v1/purchases", request, ct);

    public Task<PurchaseItem> ConfirmPurchaseAsync(Guid id, CancellationToken ct = default) =>
        api.SendJsonAsync<PurchaseItem>(HttpMethod.Post, $"api/v1/purchases/{id:D}/confirm", new { }, ct);

    public Task<PagedResult<PurchaseItem>> ListPurchasesAsync(int page, int pageSize, string? search,
        string? status, CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        if (!string.IsNullOrWhiteSpace(status)) query.Add("status=" + Uri.EscapeDataString(status.Trim()));
        return api.GetAsync<PagedResult<PurchaseItem>>($"api/v1/purchases?{string.Join('&', query)}", ct);
    }

    public Task<IReadOnlyList<SupplierIdentificationTypeOption>> GetSupplierIdentificationTypesAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<SupplierIdentificationTypeOption>>("api/v1/suppliers/identification-types", ct);

    public Task<PagedResult<SupplierItem>> ListSuppliersAsync(
        int page, int pageSize, string? search, bool? isActive, CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        }

        if (isActive is not null)
        {
            query.Add("isActive=" + (isActive.Value ? "true" : "false"));
        }

        return api.GetAsync<PagedResult<SupplierItem>>($"api/v1/suppliers?{string.Join('&', query)}", ct);
    }

    public Task<SupplierItem> CreateSupplierAsync(SaveSupplier request, CancellationToken ct = default) =>
        api.SendJsonAsync<SupplierItem>(HttpMethod.Post, "api/v1/suppliers", request, ct);

    public Task<SupplierItem> UpdateSupplierAsync(Guid id, SaveSupplier request, CancellationToken ct = default) =>
        api.SendJsonAsync<SupplierItem>(HttpMethod.Put, $"api/v1/suppliers/{id}", request, ct);

    public Task<SupplierItem> SetSupplierStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<SupplierItem>(HttpMethod.Patch, $"api/v1/suppliers/{id}/status", new ChangeSupplierStatus(isActive), ct);
}
