namespace ERP.Modules.Inventory.Contracts;

public sealed record ProductSummary(Guid ProductId, string Code, string Name, bool IsService, string UnitCode, bool IsActive);

public sealed record StockLevel(Guid ProductId, Guid WarehouseId, decimal QuantityOnHand, decimal QuantityReserved);

public enum InventoryMovementKind
{
    Entrada = 1,
    Salida = 2,
    Ajuste = 3,
    Transferencia = 4,
}

/// <param name="IdempotencyKey">Clave estable del documento origen; reintentos no duplican movimientos.</param>
public sealed record InventoryMovementLine(Guid ProductId, decimal Quantity, decimal? UnitCost);

public sealed record InventoryMovementRequest(
    string IdempotencyKey,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    Guid WarehouseId,
    InventoryMovementKind Kind,
    DateOnly MovementDate,
    IReadOnlyList<InventoryMovementLine> Lines);

public sealed record InventoryMovementResult(Guid MovementId, bool AlreadyProcessed, decimal TotalCost);

/// <summary>Consultas de catálogo y existencias. Estado: contrato definido, implementación pendiente (Integrante 5).</summary>
public interface IProductCatalog
{
    Task<IReadOnlyList<ProductSummary>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

public interface IStockQueryService
{
    Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken);
}

/// <summary>
/// Registro de movimientos solicitado por Ventas/Compras. Valida stock (política de negativos y costeo
/// pendientes: docs/decisiones-pendientes.md) con control de concurrencia.
/// </summary>
public interface IInventoryMovementService
{
    Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken);
}
