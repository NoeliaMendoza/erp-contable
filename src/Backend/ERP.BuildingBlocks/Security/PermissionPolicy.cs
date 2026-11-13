namespace ERP.BuildingBlocks.Security;

/// <summary>Convención de nombres de políticas de autorización basadas en permisos por empresa.</summary>
public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string Name(string permissionCode) => Prefix + permissionCode;

    public static bool TryParse(string policyName, out string permissionCode)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal) && policyName.Length > Prefix.Length)
        {
            permissionCode = policyName[Prefix.Length..];
            return true;
        }

        permissionCode = string.Empty;
        return false;
    }
}

/// <summary>
/// Marca una acción/controlador que exige empresa activa (cabecera <c>X-Tenant-Id</c> validada).
/// La API responde 400 si falta la selección de empresa.
/// </summary>
public interface IRequiresTenant;
