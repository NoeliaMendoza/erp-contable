using ERP.Infrastructure.Web;
using ERP.Modules.Sales.Dtos;
using ERP.Modules.Sales.Services;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Sales.Controllers;

[Route(ApiRoutes.V1 + "/sales")]
public sealed class SalesController(SaleService service) : ApiControllerBase
{
    [HttpPost("confirm")]
    [RequirePermission(SalesPermissions.InvoicesIssue)]
    public Task<SaleResponse> Confirm(ConfirmSaleRequest request, CancellationToken ct) =>
        service.ConfirmAsync(request, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(SalesPermissions.InvoicesRead)]
    public Task<SaleResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
}
