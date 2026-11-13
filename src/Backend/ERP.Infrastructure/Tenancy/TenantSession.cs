using ERP.BuildingBlocks.Tenancy;

namespace ERP.Infrastructure.Tenancy;

/// <summary>
/// Implementación con alcance por solicitud (scoped) de <see cref="ITenantContext"/>.
/// Solo la infraestructura de autenticación/resolución de tenant debe llamar a los métodos de escritura.
/// </summary>
public sealed class TenantSession : ITenantContext
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public Guid? UserId { get; private set; }

    public Guid? TenantId { get; private set; }

    public IReadOnlySet<string> Permissions { get; private set; } = Empty;

    /// <summary>Usuario autenticado (sin tenant todavía).</summary>
    public void SetUser(Guid userId) => UserId = userId;

    /// <summary>
    /// Fija un tenant <b>candidato</b> antes de validar la membresía. Solo restringe lo que RLS deja ver;
    /// no concede permisos. Si la validación falla se debe llamar a <see cref="ClearTenant"/>.
    /// </summary>
    public void SetCandidateTenant(Guid tenantId)
    {
        TenantId = tenantId;
        Permissions = Empty;
    }

    /// <summary>Confirma el tenant tras validar la membresía y carga los permisos efectivos.</summary>
    public void ConfirmTenant(Guid tenantId, IEnumerable<string> permissions)
    {
        TenantId = tenantId;
        Permissions = permissions.ToHashSet(StringComparer.Ordinal);
    }

    public void ClearTenant()
    {
        TenantId = null;
        Permissions = Empty;
    }

    /// <summary>Contexto explícito para procesos internos autorizados (migraciones, semillas, trabajos).</summary>
    public static TenantSession ForSystem(Guid? tenantId = null, Guid? userId = null) =>
        new() { TenantId = tenantId, UserId = userId };
}
