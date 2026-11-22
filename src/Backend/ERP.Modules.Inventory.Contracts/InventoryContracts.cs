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

/// <summary>
/// Existencia disponible y reservada de un artículo en una bodega.
/// La cantidad disponible se interpreta como <c>QuantityOnHand - QuantityReserved</c>.
/// </summary>
public sealed record StockLevel(Guid ProductId, Guid WarehouseId, decimal QuantityOnHand, decimal QuantityReserved);

public enum InventoryMovementKind
{
    Entrada = 1,
    Salida = 2,
    Ajuste = 3,
    Transferencia = 4,
}

/// <param name="ProductId">Artículo de la empresa activa.</param>
/// <param name="Quantity">Cantidad positiva con máximo 4 decimales.</param>
/// <param name="UnitCost">Costo unitario obligatorio para entradas costeadas y opcional para otros movimientos.</param>
public sealed record InventoryMovementLine(Guid ProductId, decimal Quantity, decimal? UnitCost);

/// <summary>
/// Solicitud explícita de movimiento. Empresa y actor vienen de <c>ITenantContext</c>;
/// ningún DTO admite cambiar el tenant.
/// </summary>
/// <param name="IdempotencyKey">Clave estable por empresa y operación; reintentos no crean otro movimiento.</param>
/// <param name="SourceModule">Módulo propietario: <c>purchases</c> o <c>sales</c>.</param>
/// <param name="SourceDocumentType">Tipo de documento: recepción, compra, venta o ajuste.</param>
/// <param name="SourceDocumentId">Identificador persistente del documento de la empresa activa.</param>
/// <param name="WarehouseId">Bodega de la empresa activa.</param>
/// <param name="Kind">Entrada, salida, ajuste o transferencia.</param>
/// <param name="Lines">Una línea por artículo; los servicios y artículos sin control se omiten.</param>
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
    /// <summary>
    /// Consulta existencia por artículo y bodega dentro de la empresa activa.
    /// Devuelve null si no existe saldo o si el artículo/bodega no pertenece al tenant.
    /// </summary>
    Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken);
}

/// <summary>
/// Registra movimientos solicitados por Ventas/Compras.
/// Stock negativo no está permitido. El movimiento es idempotente por clave dentro de la empresa.
/// Los servicios y artículos con <c>TracksInventory=false</c> no generan líneas ni movimientos de salida/entrada.
/// </summary>
public interface IInventoryMovementService
{
    Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken);
}
