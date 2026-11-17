namespace ERP.Modules.Inventory.Dtos;

// Contrato v1 del catálogo de Inventario. TenantId nunca viaja en las solicitudes: lo fija el servidor.

public sealed record UnitResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record SaveUnitRequest(string Code, string Name);

/// <param name="Kind">"Product" o "Service".</param>
public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Kind,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    decimal Price,
    bool IsActive);

/// <param name="Kind">"Product" (producto) o "Service" (servicio).</param>
/// <param name="Price">Precio de venta en USD, máximo 4 decimales, no negativo.</param>
public sealed record SaveProductRequest(string Sku, string Name, string Kind, Guid UnitId, decimal Price);

/// <summary>Activar o desactivar (baja lógica). No existe borrado físico.</summary>
public sealed record ChangeStatusRequest(bool IsActive);
