using ERP.Infrastructure.Web;
using ERP.Modules.Platform.Dtos;
using ERP.Modules.Platform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Platform.Controllers;

/// <summary>Contribuyentes (RUC), establecimientos y puntos de emisión SRI de la empresa activa.</summary>
[Route(ApiRoutes.V1 + "/tenant/taxpayers")]
[Tags("Platform")]
public sealed class TaxpayersController(PlatformService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PlatformPermissions.TaxpayersRead)]
    public Task<IReadOnlyList<TaxpayerResponse>> List(CancellationToken ct) => service.ListTaxpayersAsync(ct);
}
