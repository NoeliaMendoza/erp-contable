using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Platform.Models;

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
}

/// <summary>Organización SaaS (cliente). No es lo mismo que contribuyente/RUC ni que sucursal.</summary>
public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Tenant Create(Guid id, string name, DateTimeOffset nowUtc) =>
        new() { Id = id, Name = name.Trim(), Status = TenantStatus.Active, CreatedAtUtc = nowUtc };
}

/// <summary>
/// Identidad global (tabla sin tenant_id, clasificada como global). Una persona puede pertenecer
/// a varios tenants con roles distintos. La integración con ASP.NET Core Identity está pendiente (ADR-0003).
/// </summary>
public sealed class AppUser
{
    private AppUser()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AppUser Create(Guid id, string email, string displayName, DateTimeOffset nowUtc) =>
        new()
        {
            Id = id,
            Email = NormalizeEmail(email),
            DisplayName = displayName.Trim(),
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public enum MembershipStatus
{
    Active = 1,
    Suspended = 2,
}

/// <summary>
/// Pertenencia de un usuario a un tenant. No implementa <see cref="ITenantOwned"/> a propósito:
/// el usuario necesita listar sus propias membresías en todos sus tenants. RLS lo permite solo
/// para filas del tenant activo o del usuario autenticado.
/// </summary>
public sealed class TenantMembership
{
    private TenantMembership()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TenantMembership Create(Guid id, Guid tenantId, Guid userId, DateTimeOffset nowUtc) =>
        new() { Id = id, TenantId = tenantId, UserId = userId, Status = MembershipStatus.Active, CreatedAtUtc = nowUtc };
}

/// <summary>Rol definido por tenant. Los permisos son códigos del catálogo de cada módulo.</summary>
public sealed class Role : ITenantOwned
{
    private Role()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsSystem { get; private set; }

    public static Role Create(Guid id, Guid tenantId, string name, bool isSystem) =>
        new() { Id = id, TenantId = tenantId, Name = name.Trim(), IsSystem = isSystem };
}

public sealed class RolePermission : ITenantOwned
{
    private RolePermission()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;

    public static RolePermission Create(Guid tenantId, Guid roleId, string permissionCode) =>
        new() { TenantId = tenantId, RoleId = roleId, PermissionCode = permissionCode };
}

public sealed class MembershipRole : ITenantOwned
{
    private MembershipRole()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid MembershipId { get; private set; }

    public Guid RoleId { get; private set; }

    public static MembershipRole Create(Guid tenantId, Guid membershipId, Guid roleId) =>
        new() { TenantId = tenantId, MembershipId = membershipId, RoleId = roleId };
}

/// <summary>Módulo habilitado para el tenant (feature flag). Los códigos los publica cada módulo.</summary>
public sealed class TenantFeature : ITenantOwned
{
    private TenantFeature()
    {
    }

    public Guid TenantId { get; private set; }

    public string FeatureCode { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public static TenantFeature Create(Guid tenantId, string featureCode, bool isEnabled) =>
        new() { TenantId = tenantId, FeatureCode = featureCode, IsEnabled = isEnabled };
}
