namespace ERP.Modules.Purchases.Dtos;

public sealed record RegisterPurchaseLineRequest(Guid ProductId, decimal Quantity, decimal UnitCost);

public sealed record RegisterPurchaseRequest(
    Guid SupplierId,
    DateOnly? PurchaseDate,
    IReadOnlyList<RegisterPurchaseLineRequest> Lines);

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
