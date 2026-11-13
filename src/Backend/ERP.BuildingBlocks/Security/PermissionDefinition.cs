namespace ERP.BuildingBlocks.Security;

/// <summary>Permiso granular por empresa, p. ej. <c>contabilidad.asientos.contabilizar</c>.</summary>
/// <param name="Code">Código estable en minúsculas: modulo.recurso.accion.</param>
/// <param name="Module">Módulo propietario.</param>
/// <param name="Description">Descripción en español para administración.</param>
public sealed record PermissionDefinition(string Code, string Module, string Description);

/// <summary>Cada módulo publica su catálogo de permisos implementando esta interfaz.</summary>
public interface IPermissionProvider
{
    IEnumerable<PermissionDefinition> GetPermissions();
}
