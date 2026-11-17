using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using ERP.Modules.Inventory.Services;
using ERP.Modules.Inventory.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Inventory.Controllers;

/// <summary>Catálogo de productos y servicios de la empresa activa. No informa existencias.</summary>
[Route(ApiRoutes.V1 + "/products")]
[Tags("Inventory")]
public sealed class ProductsController(ProductService service) : ApiControllerBase
{
    /// <param name="kind">Filtro opcional: Product o Service.</param>
    [HttpGet]
    [RequirePermission(InventoryPermissions.ProductsRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] string? kind = null, [FromQuery] bool? isActive = null,
        CancellationToken ct = default)
    {
        ProductKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!KindParser.TryParse(kind, out var parsed))
            {
                ModelState.AddModelError(nameof(kind), "El tipo debe ser Product o Service.");
                return ValidationProblem(ModelState);
            }

            kindFilter = parsed;
        }

        return Ok(await service.ListAsync(new PageRequest(page, pageSize, search), kindFilter, isActive, ct));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(InventoryPermissions.ProductsRead)]
    public Task<ProductResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(InventoryPermissions.ProductsWrite)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveProductRequest request, [FromServices] IValidator<SaveProductRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(InventoryPermissions.ProductsWrite)]
    public async Task<IActionResult> Update(Guid id, SaveProductRequest request, [FromServices] IValidator<SaveProductRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(InventoryPermissions.ProductsWrite)]
    public Task<ProductResponse> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        service.SetStatusAsync(id, request.IsActive, ct);
}
