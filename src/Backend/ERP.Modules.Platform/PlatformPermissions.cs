using ERP.BuildingBlocks.Security;

namespace ERP.Modules.Platform;

public static class PlatformPermissions
{
    public const string TenantRead = "platform.tenant.read";
    public const string BranchesRead = "platform.branches.read";
    public const string BranchesManage = "platform.branches.manage";
    public const string TaxpayersRead = "platform.taxpayers.read";
    public const string TaxpayersManage = "platform.taxpayers.manage";
    public const string UsersManage = "platform.users.manage";
    public const string RolesManage = "platform.roles.manage";

    public static readonly IReadOnlyList<string> ReadOnly = [TenantRead, BranchesRead, TaxpayersRead];

    public static readonly IReadOnlyList<string> All =
        [TenantRead, BranchesRead, BranchesManage, TaxpayersRead, TaxpayersManage, UsersManage, RolesManage];
}

public sealed class PlatformPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PlatformPermissions.TenantRead, "platform", "Consultar la empresa activa"),
        new(PlatformPermissions.BranchesRead, "platform", "Consultar sucursales"),
        new(PlatformPermissions.BranchesManage, "platform", "Crear y modificar sucursales"),
        new(PlatformPermissions.TaxpayersRead, "platform", "Consultar contribuyentes, establecimientos y puntos de emisión"),
        new(PlatformPermissions.TaxpayersManage, "platform", "Administrar contribuyentes, establecimientos y puntos de emisión"),
        new(PlatformPermissions.UsersManage, "platform", "Administrar usuarios y membresías de la empresa"),
        new(PlatformPermissions.RolesManage, "platform", "Administrar roles y permisos de la empresa"),
    ];
}
