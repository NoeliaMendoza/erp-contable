using System.Text.Json.Serialization;
using ERP.BuildingBlocks.Paging;

namespace ERP.Modules.Inventory.Dtos;

// Contrato v1 del catálogo de Inventario. TenantId nunca viaja en las solicitudes: lo fija el servidor.

public sealed record UnitResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record SaveUnitRequest(string Code, string Name);

public sealed record CategoryResponse(Guid Id, string Name, bool IsActive);

public sealed record SaveCategoryRequest(string Name);

/// <param name="Kind">"Product" o "Service".</param>
/// <param name="VatTreatment">"Standard" (gravado tarifa general), "ZeroRated" (0 %), "Exempt" (exento) o "NotSubject" (no objeto).</param>
public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Kind,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    decimal Price,
    bool IsActive,
    string VatTreatment,
    Guid? CategoryId,
    string? CategoryName,
    string? Barcode,
    string? Description,
    decimal? Cost,
    bool TracksInventory,
    decimal? MinStock);

/// <param name="Kind">"Product" (producto) o "Service" (servicio).</param>
/// <param name="Price">Precio de venta en USD, máximo 4 decimales, no negativo.</param>
/// <param name="VatTreatment">Obligatorio: "Standard", "ZeroRated", "Exempt" o "NotSubject".</param>
/// <param name="CategoryId">Opcional.</param>
/// <param name="Barcode">Opcional; único por empresa.</param>
/// <param name="Cost">Costo referencial opcional, USD, máximo 4 decimales.</param>
/// <param name="TracksInventory">Opcional: por defecto true para productos y false para servicios. Un servicio no puede ser true.</param>
/// <param name="MinStock">Opcional; solo si controla inventario.</param>
public sealed record SaveProductRequest(
    string Sku,
    string Name,
    string Kind,
    [property: JsonRequired] Guid UnitId,
    [property: JsonRequired] decimal Price,
    string? VatTreatment,
    Guid? CategoryId = null,
    string? Barcode = null,
    string? Description = null,
    decimal? Cost = null,
    bool? TracksInventory = null,
    decimal? MinStock = null);

/// <summary>Activar o desactivar (baja lógica). No existe borrado físico. Obligatorio: sin él no se asume false.</summary>
public sealed record ChangeStatusRequest([property: JsonRequired] bool IsActive);

// ── Bodegas, existencias y kardex (INV-03) ─────────────────────────────────────────────────────

public sealed record WarehouseResponse(Guid Id, string Code, string Name, bool IsDefault, bool IsActive);

/// <param name="Code">Único por empresa; letras, números, guion o guion bajo. Se guarda en mayúsculas.</param>
public sealed record CreateWarehouseRequest(string Code, string Name);

public sealed record RenameWarehouseRequest(string Name);

/// <summary>Existencia de un artículo que controla inventario en una bodega. Valor = existencia × costo promedio.</summary>
public sealed record StockItemResponse(
    Guid ProductId,
    string Sku,
    string Name,
    string UnitCode,
    bool IsActive,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal Available,
    decimal AverageCost,
    decimal TotalValue,
    decimal? MinStock,
    bool BelowMinimum);

/// <summary>Existencias de una bodega con el valor total del inventario mostrado.</summary>
public sealed record StockReportResponse(WarehouseResponse Warehouse, PagedResult<StockItemResponse> Items);

/// <summary>Fila del kardex: una línea de movimiento con el saldo que dejó.</summary>
/// <param name="Kind">"Entrada" o "Salida".</param>
/// <param name="QuantityAfter">Null en movimientos registrados antes de INV-03.</param>
public sealed record KardexEntryResponse(
    Guid MovementId,
    long Sequence,
    DateOnly MovementDate,
    DateTimeOffset RegisteredAtUtc,
    string Kind,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal? UnitCost,
    decimal LineCost,
    decimal? QuantityAfter,
    decimal? AverageCostAfter);

/// <summary>Kardex de un artículo en una bodega, del movimiento más antiguo al más reciente.</summary>
public sealed record KardexResponse(
    Guid ProductId,
    string Sku,
    string Name,
    string UnitCode,
    WarehouseResponse Warehouse,
    decimal QuantityOnHand,
    decimal AverageCost,
    PagedResult<KardexEntryResponse> Entries);
