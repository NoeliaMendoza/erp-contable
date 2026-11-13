using ERP.Infrastructure.Web;
using ERP.Modules.Platform.Dtos;
using ERP.Modules.Platform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Platform.Controllers;

/// <summary>Usuario autenticado y empresas a las que pertenece (no requiere empresa activa).</summary>
[Route(ApiRoutes.V1 + "/me")]
[Tags("Platform")]
public sealed class MeController(PlatformService service) : ApiControllerBase
{
    [HttpGet]
    public Task<MyProfileResponse> Get(CancellationToken ct) => service.GetMyProfileAsync(ct);
}
