using System.Text.Json.Serialization;

namespace ERP.Modules.Purchases.Dtos;

public sealed record RegisterPurchaseLineRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity,
    [property: JsonRequired] decimal UnitCost);

public sealed record RegisterPurchaseRequest(
    [property: JsonRequired] Guid SupplierId,
    DateOnly? PurchaseDate,
    [property: JsonRequired] IReadOnlyList<RegisterPurchaseLineRequest> Lines);

public sealed record PurchaseLineResponse(
    Guid Id,
    Guid ProductId,
    string Code,
    string Name,
    string UnitCode,
    decimal Quantity,
    decimal UnitCost,
    decimal LineCost);

public sealed record PurchaseResponse(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    DateOnly PurchaseDate,
    Guid WarehouseId,
    string Status,
    decimal TotalCost,
    Guid? StockMovementId,
    Guid? JournalEntryId,
    IReadOnlyList<PurchaseLineResponse> Lines);
