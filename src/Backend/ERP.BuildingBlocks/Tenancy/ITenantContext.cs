namespace ERP.BuildingBlocks.Tenancy;

/// <summary>
/// Contexto confiable de la solicitud actual. Lo construye el backend a partir de la
/// identidad autenticada y de una membresía verificada; nunca a partir de un valor
/// enviado por el cliente sin validar.
/// </summary>
public interface ITenantContext
{
    /// <summary>Usuario autenticado (identidad global), si existe.</summary>
    Guid? UserId { get; }

    /// <summary>Tenant activo validado. Nulo cuando la solicitud no tiene tenant seleccionado.</summary>
    Guid? TenantId { get; }

    bool HasTenant => TenantId.HasValue;

    /// <summary>Permisos efectivos del usuario en el tenant activo.</summary>
    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);

    /// <summary>Devuelve el tenant activo o lanza si no existe (uso en casos de uso tenant-scoped).</summary>
    Guid RequireTenantId() =>
        TenantId ?? throw new TenantContextMissingException();
}

public sealed class TenantContextMissingException()
    : InvalidOperationException("La operación requiere una empresa (tenant) activa y validada.");
