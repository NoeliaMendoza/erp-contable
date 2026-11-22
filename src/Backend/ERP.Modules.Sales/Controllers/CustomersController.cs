using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Sales.Dtos;
using ERP.Modules.Sales.Services;
using ERP.Modules.Sales.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Sales.Controllers;

[Route(ApiRoutes.V1 + "/customers")]
public sealed class CustomersController(CustomerService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(SalesPermissions.CustomersRead)]
    public Task<PagedResult<CustomerResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), isActive, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(SalesPermissions.CustomersRead)]
    public Task<CustomerResponse> Get(Guid id, CancellationToken ct) =>
        service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(SalesPermissions.CustomersWrite)]
    public async Task<IActionResult> Create(
        SaveCustomerRequest request,
        [FromServices] IValidator<SaveCustomerRequest> validator,
        CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(SalesPermissions.CustomersWrite)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveCustomerRequest request,
        [FromServices] IValidator<SaveCustomerRequest> validator,
        CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }
        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(SalesPermissions.CustomersWrite)]
    public Task<CustomerResponse> ChangeStatus(
        Guid id,
        ChangeCustomerStatusRequest request,
        CancellationToken ct) =>
        service.SetStatusAsync(id, request.IsActive, ct);
}
