using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Consultas de pantalla: existencias por bodega y kardex por artículo. Solo lectura.</summary>
public sealed class StockReportService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>Artículos que controlan inventario con su existencia en la bodega (0 si nunca tuvieron movimientos).</summary>
    public Task<StockReportResponse> GetStockAsync(Guid? warehouseId, PageRequest page, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var warehouse = await ResolveWarehouseAsync(warehouseId, ct);
            var products = db.Products.AsNoTracking().Where(p => p.TracksInventory);
            if (isActive is not null)
            {
                products = products.Where(p => p.IsActive == isActive);
            }

            if (page.Term is { } term)
            {
                var pattern = SearchPattern.Contains(term);
                products = products.Where(p => EF.Functions.ILike(p.Sku, pattern, "\\") || EF.Functions.ILike(p.Name, pattern, "\\"));
            }

            var query =
                from p in products
                join u in db.Units.AsNoTracking() on p.UnitId equals u.Id
                join s in db.StockBalances.AsNoTracking().Where(s => s.WarehouseId == warehouse.Id) on p.Id equals s.ProductId into balances
                from s in balances.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.Sku,
                    p.Name,
                    UnitCode = u.Code,
                    p.IsActive,
                    p.MinStock,
                    OnHand = s == null ? 0m : s.QuantityOnHand,
                    Reserved = s == null ? 0m : s.QuantityReserved,
                    Average = s == null ? 0m : s.AverageCost,
                };

            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(r => r.Sku).ThenBy(r => r.Id)
                .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
            var items = rows.Select(r => new StockItemResponse(
                    r.Id, r.Sku, r.Name, r.UnitCode, r.IsActive, r.OnHand, r.Reserved, r.OnHand - r.Reserved, r.Average,
                    StockBalance.RoundAmount(r.OnHand * r.Average), r.MinStock, r.MinStock is { } min && r.OnHand < min))
                .ToList();

            return new StockReportResponse(WarehouseService.ToResponse(warehouse),
                new PagedResult<StockItemResponse>(items, total, page.SafePage, page.SafePageSize));
        }, cancellationToken);

    /// <summary>Kardex del artículo en la bodega, en el orden en que se registraron los movimientos.</summary>
    public Task<KardexResponse> GetKardexAsync(Guid productId, Guid? warehouseId, DateOnly? from, DateOnly? to, PageRequest page, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var product = await (
                    from p in db.Products.AsNoTracking()
                    join u in db.Units.AsNoTracking() on p.UnitId equals u.Id
                    where p.Id == productId
                    select new { p.Id, p.Sku, p.Name, UnitCode = u.Code })
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Artículo", productId);
            var warehouse = await ResolveWarehouseAsync(warehouseId, ct);
            var balance = await db.StockBalances.AsNoTracking()
                .SingleOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouse.Id, ct);

            var query =
                from l in db.InventoryMovementLines.AsNoTracking()
                join m in db.InventoryMovements.AsNoTracking() on l.MovementId equals m.Id
                where l.ProductId == productId && m.WarehouseId == warehouse.Id
                select new { Line = l, Movement = m };
            if (from is not null)
            {
                query = query.Where(x => x.Movement.MovementDate >= from);
            }

            if (to is not null)
            {
                query = query.Where(x => x.Movement.MovementDate <= to);
            }

            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.Line.Sequence).ThenBy(x => x.Movement.CreatedAtUtc).ThenBy(x => x.Line.Id)
                .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
            var entries = rows.Select(x => new KardexEntryResponse(
                    x.Movement.Id, x.Line.Sequence, x.Movement.MovementDate, x.Movement.CreatedAtUtc, x.Movement.Kind.ToString(),
                    x.Movement.SourceModule, x.Movement.SourceDocumentType, x.Movement.SourceDocumentId,
                    x.Movement.Kind == InventoryMovementKind.Entrada ? x.Line.Quantity : 0m,
                    x.Movement.Kind == InventoryMovementKind.Salida ? x.Line.Quantity : 0m,
                    x.Line.UnitCost, x.Line.LineCost, x.Line.QuantityAfter, x.Line.AverageCostAfter))
                .ToList();

            return new KardexResponse(product.Id, product.Sku, product.Name, product.UnitCode, WarehouseService.ToResponse(warehouse),
                balance?.QuantityOnHand ?? 0m, balance?.AverageCost ?? 0m,
                new PagedResult<KardexEntryResponse>(entries, total, page.SafePage, page.SafePageSize));
        }, cancellationToken);

    private async Task<Warehouse> ResolveWarehouseAsync(Guid? warehouseId, CancellationToken ct) =>
        warehouseId is { } id
            ? await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Bodega", id)
            : await InventoryStockService.EnsureDefaultWarehouseAsync(db, tenant, clock, ct);
}
