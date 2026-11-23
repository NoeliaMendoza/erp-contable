using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Inventory.Controllers;

/// <summary>
/// Bodegas, existencias y kardex de la empresa activa. No hay endpoint para registrar movimientos:
/// solo Compras y Ventas mueven stock, a través de <c>IInventoryMovementService</c> (ACUERDO-01).
/// </summary>
[Tags("Inventory")]
public sealed class StockController(WarehouseService warehouses, StockReportService reports) : ApiControllerBase
{
    [HttpGet(ApiRoutes.V1 + "/warehouses")]
    [RequirePermission(InventoryPermissions.KardexRead)]
    public Task<IReadOnlyList<WarehouseResponse>> ListWarehouses(CancellationToken ct) => warehouses.ListAsync(ct);

    [HttpPost(ApiRoutes.V1 + "/warehouses")]
    [RequirePermission(InventoryPermissions.WarehousesManage)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWarehouse(CreateWarehouseRequest request, [FromServices] IValidator<CreateWarehouseRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await warehouses.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut(ApiRoutes.V1 + "/warehouses/{id:guid}")]
    [RequirePermission(InventoryPermissions.WarehousesManage)]
    public async Task<IActionResult> RenameWarehouse(Guid id, RenameWarehouseRequest request, [FromServices] IValidator<RenameWarehouseRequest> validator, CancellationToken ct)
    {
        if (await this.ValidateAsync(validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await warehouses.RenameAsync(id, request, ct));
    }

    [HttpPatch(ApiRoutes.V1 + "/warehouses/{id:guid}/status")]
    [RequirePermission(InventoryPermissions.WarehousesManage)]
    public Task<WarehouseResponse> ChangeWarehouseStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        warehouses.SetStatusAsync(id, request.IsActive, ct);

    /// <summary>Existencias por bodega; sin <paramref name="warehouseId"/> usa la bodega principal.</summary>
    [HttpGet(ApiRoutes.V1 + "/stock")]
    [RequirePermission(InventoryPermissions.KardexRead)]
    public Task<StockReportResponse> Stock(
        [FromQuery] Guid? warehouseId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] bool? isActive = null, CancellationToken ct = default) =>
        reports.GetStockAsync(warehouseId, new PageRequest(page, pageSize, search), isActive, ct);

    /// <summary>Kardex del artículo; sin <paramref name="warehouseId"/> usa la bodega principal. Fechas inclusivas.</summary>
    [HttpGet(ApiRoutes.V1 + "/products/{productId:guid}/kardex")]
    [RequirePermission(InventoryPermissions.KardexRead)]
    public Task<KardexResponse> Kardex(
        Guid productId, [FromQuery] Guid? warehouseId = null, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken ct = default) =>
        reports.GetKardexAsync(productId, warehouseId, from, to, new PageRequest(page, pageSize), ct);
}
