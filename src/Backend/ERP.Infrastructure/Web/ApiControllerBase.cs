using ERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Infrastructure.Web;

/// <summary>Prefijos de ruta versionados de la API.</summary>
public static class ApiRoutes
{
    public const string V1 = "api/v1";
}

/// <summary>
/// Base de todos los controladores (la "C" de MVC). Reglas:
/// solo transporte HTTP, validación de entrada y permisos; la lógica va en <c>Services</c>
/// y las reglas de negocio en <c>Models</c>. Nunca devolver entidades EF: usar <c>Dtos</c>.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase;

/// <summary>
/// Exige empresa activa y el permiso indicado en esa empresa. Úselo en cada acción o controlador
/// de un módulo: ocultar botones en el front es solo UX, la seguridad está aquí.
/// <code>[RequirePermission(PlatformPermissions.BranchesRead)]</code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permissionCode)
    : AuthorizeAttribute(PermissionPolicy.Name(permissionCode)), IRequiresTenant
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>Exige empresa activa validada, sin un permiso concreto.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAttribute : AuthorizeAttribute, IRequiresTenant;
