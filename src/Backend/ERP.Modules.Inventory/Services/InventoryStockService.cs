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
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new DomainException("movement_idempotency_required", "El movimiento requiere una clave de idempotencia.");
            if (request.SourceDocumentId == Guid.Empty)
                throw new DomainException("movement_source_document_required", "El movimiento debe identificar su documento de origen.");
            if (request.WarehouseId == Guid.Empty)
                throw new DomainException("movement_warehouse_required", "Debe seleccionar una bodega válida.");
            if (string.IsNullOrWhiteSpace(request.SourceModule) || string.IsNullOrWhiteSpace(request.SourceDocumentType))
                throw new DomainException("movement_source_required", "El movimiento debe identificar módulo y tipo de documento origen.");
            if (!Enum.IsDefined(request.Kind))
                throw new DomainException("movement_kind_invalid", "El tipo de movimiento no es válido.");
            if (request.Lines.Count == 0)
                throw new DomainException("movement_no_lines", "El movimiento debe contener al menos una línea.");
            if (request.Lines.Any(l => l.ProductId == Guid.Empty
                                      || l.Quantity <= 0m
                                      || decimal.Round(l.Quantity, 4) != l.Quantity
                                      || l.UnitCost is < 0m
                                      || (l.UnitCost is { } cost && decimal.Round(cost, 4) != cost)))
                throw new DomainException("movement_line_invalid", "Una línea del movimiento tiene cantidad, artículo o costo inválido.");

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
