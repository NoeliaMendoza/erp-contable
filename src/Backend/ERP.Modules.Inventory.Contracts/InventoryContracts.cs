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
    string? Barcode);

/// <summary>Existencias de un artículo en una bodega de la empresa activa. Se calculan a partir de los movimientos; nunca se digitan.</summary>
/// <param name="QuantityOnHand">Unidades disponibles, escala 4. Un artículo con control de inventario y sin movimientos tiene 0.</param>
/// <param name="QuantityReserved">Reservas: fuera del alcance de ACUERDO-01 (#11); siempre 0 en este incremento.</param>
public sealed record StockLevel(Guid ProductId, Guid WarehouseId, decimal QuantityOnHand, decimal QuantityReserved);

/// <summary>Bodega de la empresa activa. Cada empresa tiene una bodega principal desde INV-03.</summary>
public sealed record WarehouseSummary(Guid WarehouseId, string Code, string Name);

/// <summary>Tipo de movimiento. ACUERDO-01 (#11) habilita solo <see cref="Entrada"/> y <see cref="Salida"/>.</summary>
public enum InventoryMovementKind
{
    /// <summary>Ingreso de mercadería con costo (recepción de compra).</summary>
    Entrada = 1,

    /// <summary>Egreso valorado al costo promedio vigente (venta).</summary>
    Salida = 2,

    /// <summary>Reservado: se rechaza con <c>inventory_movement_kind_not_supported</c> hasta un acuerdo posterior.</summary>
    Ajuste = 3,

    /// <summary>Reservado: se rechaza con <c>inventory_movement_kind_not_supported</c> hasta un acuerdo posterior.</summary>
    Transferencia = 4,
}

/// <summary>Línea de un movimiento. Un artículo aparece una sola vez por solicitud: el módulo origen agrupa sus líneas.</summary>
/// <param name="ProductId">Artículo activo de la empresa activa con <see cref="ProductSummary.TracksInventory"/> = true.</param>
/// <param name="Quantity">Mayor que cero, escala máxima 4, en la unidad del artículo.</param>
/// <param name="UnitCost">USD, escala máxima 4. Obligatorio y no negativo en una Entrada; null en una Salida (lo calcula Inventario).</param>
public sealed record InventoryMovementLine(Guid ProductId, decimal Quantity, decimal? UnitCost);

/// <summary>
/// Solicitud de movimiento de un documento de negocio. La empresa y el usuario se obtienen del ITenantContext
/// confiable del servidor, nunca de un campo del DTO.
/// </summary>
/// <param name="IdempotencyKey">Clave estable <c>&lt;modulo&gt;:&lt;tipo&gt;:&lt;uuid&gt;</c> del documento; el emisor la conserva en cada reintento.</param>
/// <param name="SourceModule"><c>purchases</c> o <c>sales</c> en este incremento.</param>
/// <param name="SourceDocumentType"><c>receipt</c> (recepción de compra) o <c>invoice</c> (venta).</param>
/// <param name="SourceDocumentId">UUID del documento origen de la empresa activa, para el kardex.</param>
/// <param name="WarehouseId">Bodega de la empresa activa; el documento origen la guarda para repetirla en los reintentos.</param>
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
/// <param name="AlreadyProcessed">true si la clave ya existía con el mismo contenido y se devolvió el movimiento original.</param>
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

/// <summary>
/// Consulta de existencias de la empresa activa. Estado: acuerdo ACUERDO-01 (#11) propuesto, implementación en INV-03 (#15).
/// Sirve para mostrar disponibilidad; no garantiza el stock al confirmar: eso lo valida <see cref="IInventoryMovementService"/>.
/// </summary>
public interface IStockQueryService
{
    /// <summary>Existencias de un artículo; null si no existe en la empresa activa o no controla inventario.</summary>
    Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>Existencias de varios artículos con control de inventario; los ajenos, inexistentes o sin control se omiten.</summary>
    Task<IReadOnlyList<StockLevel>> GetStockLevelsAsync(IReadOnlyCollection<Guid> productIds, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>Bodega principal de la empresa activa, para documentos que no eligen bodega.</summary>
    Task<WarehouseSummary> GetDefaultWarehouseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Único camino para que Compras y Ventas muevan stock; ningún módulo inserta en tablas de Inventario.
/// Valida artículos, bodega y existencias (sin stock negativo) con control de concurrencia, valora al costo
/// promedio ponderado móvil y es idempotente por empresa y clave. Participa en la transacción del caso de uso
/// que lo invoca: no hace commit propio. Las reglas completas están en docs/contratos-modulos.md (ACUERDO-01).
/// Estado: firmas definidas, implementación en INV-03 (#15).
/// </summary>
public interface IInventoryMovementService
{
    Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken);
}
