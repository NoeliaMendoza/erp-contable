using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Purchases.Controllers;

[Route(ApiRoutes.V1 + "/purchases")]
[Tags("Purchases")]
public sealed class PurchasesController(PurchaseService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PurchasesPermissions.DocumentsRead)]
    public Task<PagedResult<PurchaseResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), status, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(PurchasesPermissions.DocumentsRead)]
    public Task<PurchaseResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(PurchasesPermissions.DocumentsRegister)]
    [ProducesResponseType<PurchaseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterPurchaseRequest request,
        [FromServices] IValidator<RegisterPurchaseRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var created = await service.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
