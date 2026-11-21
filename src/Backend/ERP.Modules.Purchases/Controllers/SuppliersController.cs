using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Services;
using ERP.Tributario.Identification;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Purchases.Controllers;

[Route(ApiRoutes.V1 + "/suppliers")]
[Tags("Purchases")]
public sealed class SuppliersController(SupplierService service) : ApiControllerBase
{
    [HttpGet("identification-types")]
    [RequirePermission(PurchasesPermissions.SuppliersRead)]
    public IReadOnlyList<SupplierIdentificationTypeResponse> IdentificationTypes() =>
        EcuadorIdentificationCatalog.ForSuppliers
            .Select(type => new SupplierIdentificationTypeResponse(type.SriCode, type.Name))
            .ToArray();

    [HttpGet]
    [RequirePermission(PurchasesPermissions.SuppliersRead)]
    public Task<PagedResult<SupplierResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), isActive, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(PurchasesPermissions.SuppliersRead)]
    public Task<SupplierResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(PurchasesPermissions.SuppliersWrite)]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveSupplierRequest request, [FromServices] IValidator<SaveSupplierRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateSupplierAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PurchasesPermissions.SuppliersWrite)]
    public async Task<IActionResult> Update(Guid id, SaveSupplierRequest request, [FromServices] IValidator<SaveSupplierRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateSupplierAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(PurchasesPermissions.SuppliersWrite)]
    public Task<SupplierResponse> ChangeStatus(Guid id, ChangeSupplierStatusRequest request, CancellationToken ct) =>
        service.SetStatusAsync(id, request.IsActive, ct);
}

internal static class SupplierValidationExtensions
{
    public static async Task<IActionResult?> ValidateSupplierAsync(
        this ControllerBase controller,
        IValidator<SaveSupplierRequest> validator,
        SaveSupplierRequest request,
        CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid)
        {
            return null;
        }

        foreach (var error in result.Errors)
        {
            controller.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return controller.ValidationProblem(controller.ModelState);
    }
}
