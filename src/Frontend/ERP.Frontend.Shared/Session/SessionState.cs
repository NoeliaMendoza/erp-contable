namespace ERP.Frontend.Shared.Session;

/// <summary>
/// Estado de sesión del cliente. Los permisos se usan solo para UX (mostrar u ocultar acciones);
/// la API vuelve a validarlos en cada solicitud.
/// </summary>
public sealed class SessionState
{
    public string? UserEmail { get; private set; }

    public string? DisplayName { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? TenantName { get; private set; }

    public IReadOnlySet<string> Permissions { get; private set; } = new HashSet<string>();

    public IReadOnlySet<string> EnabledFeatures { get; private set; } = new HashSet<string>();

    public void SignIn(string email) => UserEmail = email.Trim();

    public void SetProfile(string displayName) => DisplayName = displayName;

    public void SelectTenant(Guid tenantId, string tenantName)
    {
        TenantId = tenantId;
        TenantName = tenantName;
        Permissions = new HashSet<string>();
        EnabledFeatures = new HashSet<string>();
    }

    public void SetTenantDetails(IEnumerable<string> permissions, IEnumerable<string> features)
    {
        Permissions = permissions.ToHashSet(StringComparer.Ordinal);
        EnabledFeatures = features.ToHashSet(StringComparer.Ordinal);
    }

    public bool Can(string permission) => Permissions.Contains(permission);

    public void ClearTenant()
    {
        TenantId = null;
        TenantName = null;
        Permissions = new HashSet<string>();
        EnabledFeatures = new HashSet<string>();
    }

    public void SignOut()
    {
        ClearTenant();
        UserEmail = null;
        DisplayName = null;
    }
}
