using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Inventory.Controllers;

/// <summary>Unidades de medida de la empresa activa.</summary>
[Route(ApiRoutes.V1 + "/units")]
[Tags("Inventory")]
public sealed class UnitsController(UnitService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(InventoryPermissions.UnitsRead)]
    public Task<PagedResult<UnitResponse>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] bool? isActive = null, CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), isActive, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(InventoryPermissions.UnitsRead)]
    public Task<UnitResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(InventoryPermissions.UnitsWrite)]
    [ProducesResponseType<UnitResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveUnitRequest request, [FromServices] IValidator<SaveUnitRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(InventoryPermissions.UnitsWrite)]
    public async Task<IActionResult> Update(Guid id, SaveUnitRequest request, [FromServices] IValidator<SaveUnitRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(InventoryPermissions.UnitsWrite)]
    public Task<UnitResponse> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        service.SetStatusAsync(id, request.IsActive, ct);
}
