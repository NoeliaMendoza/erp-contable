using ERP.Infrastructure.Web;
using ERP.Modules.Platform.Dtos;
using ERP.Modules.Platform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Platform.Controllers;

/// <summary>Empresa activa: datos, módulos habilitados y permisos del usuario.</summary>
[Route(ApiRoutes.V1 + "/tenant")]
[Tags("Platform")]
public sealed class TenantController(PlatformService service) : ApiControllerBase
{
    [HttpGet("current")]
    [RequirePermission(PlatformPermissions.TenantRead)]
    public Task<CurrentTenantResponse> GetCurrent(CancellationToken ct) => service.GetCurrentTenantAsync(ct);
}
