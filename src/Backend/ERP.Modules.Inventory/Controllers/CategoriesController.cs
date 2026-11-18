using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Inventory.Controllers;

/// <summary>Categorías del catálogo de la empresa activa.</summary>
[Route(ApiRoutes.V1 + "/categories")]
[Tags("Inventory")]
public sealed class CategoriesController(CategoryService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(InventoryPermissions.CategoriesRead)]
    public Task<PagedResult<CategoryResponse>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] bool? isActive = null, CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), isActive, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(InventoryPermissions.CategoriesRead)]
    public Task<CategoryResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(InventoryPermissions.CategoriesWrite)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, [FromServices] IValidator<SaveCategoryRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(InventoryPermissions.CategoriesWrite)]
    public async Task<IActionResult> Update(Guid id, SaveCategoryRequest request, [FromServices] IValidator<SaveCategoryRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(InventoryPermissions.CategoriesWrite)]
    public Task<CategoryResponse> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        service.SetStatusAsync(id, request.IsActive, ct);
}
