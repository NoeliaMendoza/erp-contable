using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

public sealed class InventoryStockService(InventoryDbContext db, ITenantContext tenant) :
    IStockQueryService, IInventoryMovementService
{
    public Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var stock = await db.StockBalances.AsNoTracking()
                .SingleOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, ct);
            return stock is null ? null : new StockLevel(stock.ProductId, stock.WarehouseId, stock.QuantityOnHand, stock.QuantityReserved);
        }, cancellationToken);

    public Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var tenantId = tenant.RequireTenantId();
            var existing = await db.InventoryMovements.AsNoTracking()
                .SingleOrDefaultAsync(m => m.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
                return new InventoryMovementResult(existing.Id, true, existing.TotalCost);

            var movement = InventoryMovement.Create(Guid.CreateVersion7(), tenantId, request);
            db.InventoryMovements.Add(movement);
            decimal totalCost = 0m;

            foreach (var line in request.Lines)
            {
                var product = await db.Products.SingleOrDefaultAsync(p => p.Id == line.ProductId && p.IsActive, ct)
                    ?? throw new NotFoundException("Artículo", line.ProductId);
                if (product.Kind == ProductKind.Service || !product.TracksInventory)
                    continue;

                var stock = await db.StockBalances
                    .SingleOrDefaultAsync(s => s.ProductId == line.ProductId && s.WarehouseId == request.WarehouseId, ct);
                if (stock is null)
                {
                    stock = StockBalance.Create(tenantId, line.ProductId, request.WarehouseId);
                    db.StockBalances.Add(stock);
                }

                if (request.Kind == InventoryMovementKind.Salida)
                    stock.Adjust(-line.Quantity);
                else
                    stock.Adjust(line.Quantity);

                var entity = InventoryMovementLineEntity.Create(tenantId, movement.Id, line.ProductId, line.Quantity, line.UnitCost);
                db.InventoryMovementLines.Add(entity);
                movement.AddLine(entity);

                if (line.UnitCost is { } cost)
                    totalCost += decimal.Round(cost * line.Quantity, 2);
            }

            movement.SetTotalCost(totalCost);
            await db.SaveChangesAsync(ct);
            return new InventoryMovementResult(movement.Id, false, totalCost);
        }, cancellationToken);
}
