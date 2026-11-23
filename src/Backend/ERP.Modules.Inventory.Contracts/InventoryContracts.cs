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
/// <param name="Price">Precio de venta de la ficha, sin IVA.</param>
/// <param name="Cost">
/// Costo referencial de la ficha (para margen). No es el costo de inventario: el costo de una salida lo calcula
/// Inventario y lo devuelve en <see cref="InventoryMovementResult.TotalCost"/> (ACUERDO-01 y ACUERDO-02).
/// </param>
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
/// Existencias de un artículo en una bodega de la empresa activa. Se calculan a partir de los movimientos; nunca se digitan.
/// La cantidad disponible es <c>QuantityOnHand - QuantityReserved</c>.
/// </summary>
/// <param name="QuantityOnHand">Unidades físicas, escala 4.</param>
/// <param name="QuantityReserved">Reservas: fuera del alcance de ACUERDO-01 (#11); 0 en este incremento.</param>
public sealed record StockLevel(Guid ProductId, Guid WarehouseId, decimal QuantityOnHand, decimal QuantityReserved);

/// <summary>Tipo de movimiento. ACUERDO-01 (#11) habilita solo <see cref="Entrada"/> y <see cref="Salida"/>.</summary>
public enum InventoryMovementKind
{
    /// <summary>Ingreso de mercadería con costo (recepción de compra).</summary>
    Entrada = 1,

    /// <summary>Egreso valorado al costo promedio vigente (venta).</summary>
    Salida = 2,

    /// <summary>Reservado: fuera de ACUERDO-01; se habilita con un acuerdo posterior.</summary>
    Ajuste = 3,

    /// <summary>Reservado: fuera de ACUERDO-01; se habilita con un acuerdo posterior.</summary>
    Transferencia = 4,
}

/// <summary>Línea de un movimiento. Un artículo aparece una sola vez por solicitud: el módulo origen agrupa sus líneas.</summary>
/// <param name="ProductId">Artículo activo de la empresa activa con <see cref="ProductSummary.TracksInventory"/> = true.</param>
/// <param name="Quantity">Mayor que cero, escala máxima 4, en la unidad del artículo.</param>
/// <param name="UnitCost">USD, escala máxima 4. Obligatorio y no negativo en una Entrada; null en una Salida (lo calcula Inventario).</param>
public sealed record InventoryMovementLine(Guid ProductId, decimal Quantity, decimal? UnitCost);

/// <summary>
/// Solicitud de movimiento de un documento de negocio. La empresa y el usuario se obtienen del ITenantContext
/// confiable del servidor; ningún DTO admite cambiar el tenant.
/// </summary>
/// <param name="IdempotencyKey">Clave estable <c>&lt;modulo&gt;:&lt;tipo&gt;:&lt;uuid&gt;</c> del documento; el emisor la conserva en cada reintento.</param>
/// <param name="SourceModule"><c>purchases</c> o <c>sales</c>.</param>
/// <param name="SourceDocumentType"><c>receipt</c> (recepción de compra) o <c>invoice</c> (venta).</param>
/// <param name="SourceDocumentId">UUID del documento origen de la empresa activa, para el kardex.</param>
/// <param name="WarehouseId">Bodega de la empresa activa; el documento origen la guarda para repetirla en los reintentos.</param>
/// <param name="Kind">Entrada o Salida (ACUERDO-01).</param>
/// <param name="MovementDate">Fecha local de Ecuador del documento. Los movimientos se valoran en el orden en que se registran.</param>
/// <param name="Lines">Al menos una línea; sin artículos repetidos.</param>
public sealed record InventoryMovementRequest(
    string IdempotencyKey,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    Guid WarehouseId,
    InventoryMovementKind Kind,
    DateOnly MovementDate,
    IReadOnlyList<InventoryMovementLine> Lines);

/// <summary>Resultado del movimiento; no sustituye el commit global del caso de uso llamador.</summary>
/// <param name="MovementId">ID del movimiento; se conserva al repetir la misma solicitud.</param>
/// <param name="AlreadyProcessed">true si la clave ya existía y se devolvió el movimiento original.</param>
/// <param name="TotalCost">
/// Costo total en USD con 2 decimales (redondeo AwayFromZero). Entrada: Σ cantidad × costo unitario.
/// Salida: Σ cantidad × costo promedio vigente. En un reintento se devuelve el valor original, no se recalcula.
/// Es el importe que Compras (Inventario) y Ventas (Costo de ventas) envían en su asiento (ACUERDO-02).
/// </param>
public sealed record InventoryMovementResult(Guid MovementId, bool AlreadyProcessed, decimal TotalCost);

/// <summary>Consulta del catálogo de artículos de la empresa activa. Estado: implementado en INV-01.</summary>
public interface IProductCatalog
{
    Task<IReadOnlyList<ProductSummary>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

/// <summary>Bodega de la empresa activa. Cada empresa tiene una sola bodega principal (<c>IsDefault</c>).</summary>
public sealed record WarehouseSummary(Guid WarehouseId, string Code, string Name, bool IsDefault, bool IsActive);

/// <summary>
/// Consulta de existencias de la empresa activa. Sirve para mostrar disponibilidad; no garantiza el stock al
/// confirmar: eso lo valida <see cref="IInventoryMovementService"/>. Estado: implementado en INV-03 (#15).
/// </summary>
public interface IStockQueryService
{
    /// <summary>
    /// Existencia por artículo y bodega dentro de la empresa activa. Devuelve null si no hay saldo
    /// o si el artículo/bodega no pertenece al tenant; quien consulta interpreta null como 0 disponible.
    /// </summary>
    Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// Existencias de varios artículos en una bodega (pantalla de venta). Solo devuelve los artículos con saldo;
    /// los que faltan se interpretan como 0 disponible.
    /// </summary>
    Task<IReadOnlyList<StockLevel>> GetStocksAsync(IReadOnlyCollection<Guid> productIds, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// Bodega principal de la empresa activa. Si la empresa todavía no tiene una, se crea en ese momento
    /// (código <c>PRINCIPAL</c>). Compras y Ventas guardan en su documento la bodega que usaron.
    /// </summary>
    Task<WarehouseSummary> GetDefaultWarehouseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Único camino para que Compras y Ventas muevan stock; ningún módulo inserta en tablas de Inventario.
/// Sin stock negativo, idempotente por empresa y clave, con control de concurrencia; participa en la transacción
/// del caso de uso que lo invoca (no hace commit propio). Reglas completas: docs/contratos-modulos.md, ACUERDO-01 (#11).
/// Estado: implementado en INV-03 (#15): costo promedio, bodegas, bloqueo del saldo y códigos de error de ACUERDO-01.
/// </summary>
public interface IInventoryMovementService
{
    Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken);
}
