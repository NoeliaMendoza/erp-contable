namespace ERP.Modules.Inventory.Contracts;

/// <summary>
/// Tratamiento de IVA del artículo. NO guarda porcentajes: Ventas/Compras resuelven el código SRI y el
/// porcentaje vigente en la fecha del documento con <c>TaxRateCatalog</c> (ninguna tarifa es constante).
/// </summary>
public enum VatTreatment
{
    /// <summary>Gravado con la tarifa general vigente.</summary>
    Standard = 1,

    /// <summary>Gravado con tarifa 0 %.</summary>
    ZeroRated = 2,

    /// <summary>Exento de IVA.</summary>
    Exempt = 3,

    /// <summary>No objeto de IVA.</summary>
    NotSubject = 4,
}

/// <param name="Code">SKU (código principal en la factura).</param>
/// <param name="Barcode">Código de barras / código auxiliar; null si no tiene.</param>
/// <param name="TracksInventory">false para servicios y artículos sin control de stock: no generan movimientos.</param>
public sealed record ProductSummary(
    Guid ProductId,
    string Code,
    string Name,
    bool IsService,
    string UnitCode,
    bool IsActive,
    VatTreatment VatTreatment,
    bool TracksInventory,
    string? Barcode,
    decimal Price,
    decimal? Cost);

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
