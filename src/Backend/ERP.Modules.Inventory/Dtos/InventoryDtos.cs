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
    Guid UnitId,
    decimal Price,
    string? VatTreatment,
    Guid? CategoryId = null,
    string? Barcode = null,
    string? Description = null,
    decimal? Cost = null,
    bool? TracksInventory = null,
    decimal? MinStock = null);

/// <summary>Activar o desactivar (baja lógica). No existe borrado físico.</summary>
public sealed record ChangeStatusRequest(bool IsActive);
