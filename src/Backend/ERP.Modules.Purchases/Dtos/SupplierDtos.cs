
namespace ERP.Modules.Purchases.Dtos;

public sealed record SupplierIdentificationTypeResponse(string SriCode, string Name);

/// <summary>
/// Datos que recibe la API al crear o editar un proveedor.
/// TenantId no se recibe porque lo establece el servidor.
/// </summary>
public sealed record SaveSupplierRequest(
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address
);

/// <summary>
/// Información que la API devuelve al consultar un proveedor.
/// </summary>
public sealed record SupplierResponse(
    Guid Id,
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive
);

/// <summary>
/// Solicitud para activar o desactivar un proveedor.
/// </summary>
public sealed record ChangeSupplierStatusRequest(
    bool IsActive
);
