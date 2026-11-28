using System.Text.Json.Serialization;
using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Sales;

public sealed class SaleLineRequest(Guid productId, decimal quantity, decimal discount = 0m)
{
    [JsonRequired]
    public Guid ProductId { get; init; } = productId;

    [JsonRequired]
    public decimal Quantity { get; init; } = quantity;

    public decimal Discount { get; init; } = discount;
}

public sealed class ConfirmSaleRequest(Guid customerId, DateOnly? saleDate,
    string idempotencyKey, Guid warehouseId, IReadOnlyList<SaleLineRequest> lines)
{
    [JsonRequired]
    public Guid CustomerId { get; init; } = customerId;

    public DateOnly? SaleDate { get; init; } = saleDate;

    [JsonRequired]
    public string IdempotencyKey { get; init; } = idempotencyKey;

    [JsonRequired]
    public Guid WarehouseId { get; init; } = warehouseId;

    [JsonRequired]
    public IReadOnlyList<SaleLineRequest> Lines { get; init; } = lines;
}

public sealed record SaleLineResponse(Guid Id, Guid ProductId, string Sku, string Name, decimal Quantity,
    decimal UnitPrice, decimal Discount, decimal TaxRate, decimal TaxAmount, decimal LineTotal);

public sealed record SaleResponse(Guid Id, string Number, string CustomerName, Guid? CustomerId,
    DateOnly SaleDate, string Status, decimal Subtotal, decimal TaxTotal, decimal Total,
    string ElectronicStatus, Guid? StockMovementId, Guid? JournalEntryId,
    IReadOnlyList<SaleLineResponse> Lines);

public sealed record CustomerItem(
    Guid Id,
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive,
    string? IdentificationWarning)
{
    public string Status => IsActive ? "Activo" : "Inactivo";

    public string Display => $"{Name} — {IdentificationType} {IdentificationNumber}";
}

public sealed record SaveCustomer(
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address);

public sealed record IdentificationOption(string Value, string Label)
{
    public static readonly IReadOnlyList<IdentificationOption> All =
    [
        new("Ruc", "RUC"),
        new("Cedula", "Cédula"),
        new("Pasaporte", "Pasaporte"),
        new("ConsumidorFinal", "Consumidor final"),
    ];
}

public sealed class SalesApi(ErpApiClient api)
{
    public Task<SaleResponse> ConfirmAsync(ConfirmSaleRequest request, CancellationToken ct = default) =>
        api.SendJsonAsync<SaleResponse>(HttpMethod.Post, "api/v1/sales/confirm", request, ct);

    public Task<SaleResponse> GetAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<SaleResponse>($"api/v1/sales/{id}", ct);

    public Task<PagedResult<SaleResponse>> ListSalesAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = $"api/v1/sales?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += "&search=" + Uri.EscapeDataString(search.Trim());
        }
        return api.GetAsync<PagedResult<SaleResponse>>(query, ct);
    }

    public Task<PagedResult<CustomerItem>> ListCustomersAsync(
        int page, int pageSize, string? search, bool? isActive, CancellationToken ct = default)
    {
        var query = $"api/v1/customers?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += "&search=" + Uri.EscapeDataString(search.Trim());
        }
        if (isActive is not null)
        {
            query += "&isActive=" + (isActive.Value ? "true" : "false");
        }
        return api.GetAsync<PagedResult<CustomerItem>>(query, ct);
    }

    public Task<CustomerItem> CreateCustomerAsync(SaveCustomer request, CancellationToken ct = default) =>
        api.SendJsonAsync<CustomerItem>(HttpMethod.Post, "api/v1/customers", request, ct);

    public Task<CustomerItem> UpdateCustomerAsync(Guid id, SaveCustomer request, CancellationToken ct = default) =>
        api.SendJsonAsync<CustomerItem>(HttpMethod.Put, $"api/v1/customers/{id}", request, ct);

    public Task<CustomerItem> SetCustomerStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<CustomerItem>(HttpMethod.Patch, $"api/v1/customers/{id}/status", new { isActive }, ct);
}
