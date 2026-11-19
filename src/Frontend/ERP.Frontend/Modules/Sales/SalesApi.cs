using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Sales;

public sealed record SaleLineRequest(Guid ProductId, decimal Quantity, decimal Discount = 0m);

public sealed record ConfirmSaleRequest(string CustomerName, Guid? CustomerId, DateOnly? SaleDate,
    string IdempotencyKey, Guid WarehouseId, IReadOnlyList<SaleLineRequest> Lines);

public sealed record SaleLineResponse(Guid Id, Guid ProductId, string Sku, string Name, decimal Quantity,
    decimal UnitPrice, decimal Discount, decimal TaxRate, decimal TaxAmount, decimal LineTotal);

public sealed record SaleResponse(Guid Id, string Number, string CustomerName, Guid? CustomerId,
    DateOnly SaleDate, string Status, decimal Subtotal, decimal TaxTotal, decimal Total,
    string ElectronicStatus, Guid? StockMovementId, Guid? JournalEntryId,
    IReadOnlyList<SaleLineResponse> Lines);

public sealed class SalesApi(ErpApiClient api)
{
    public Task<SaleResponse> ConfirmAsync(ConfirmSaleRequest request, CancellationToken ct = default) =>
        api.SendJsonAsync<SaleResponse>(HttpMethod.Post, "api/v1/sales/confirm", request, ct);

    public Task<SaleResponse> GetAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<SaleResponse>($"api/v1/sales/{id}", ct);
}
