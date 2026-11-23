using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>
/// Movimientos y existencias según ACUERDO-01 (#11): costo promedio móvil, sin stock negativo, idempotencia que
/// compara el contenido y bloqueo de los saldos para que dos ventas simultáneas no consuman la misma unidad.
/// </summary>
public sealed class InventoryStockService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock) :
    IStockQueryService, IInventoryMovementService
{
    /// <summary>Origen permitido por módulo: tipo de documento y tipo de movimiento (ACUERDO-01, «Quién pide qué»).</summary>
    private static readonly Dictionary<string, (string DocumentType, InventoryMovementKind Kind)> Sources = new(StringComparer.Ordinal)
    {
        ["purchases"] = ("receipt", InventoryMovementKind.Entrada),
        ["sales"] = ("invoice", InventoryMovementKind.Salida),
    };

    public Task<StockLevel?> GetStockAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var stock = await db.StockBalances.AsNoTracking()
                .SingleOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, ct);
            return stock is null ? null : ToLevel(stock);
        }, cancellationToken);

    public Task<IReadOnlyList<StockLevel>> GetStocksAsync(IReadOnlyCollection<Guid> productIds, Guid warehouseId, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync<IReadOnlyList<StockLevel>>(async ct =>
        {
            if (productIds.Count == 0)
            {
                return [];
            }

            var ids = productIds.Distinct().ToArray();
            var stocks = await db.StockBalances.AsNoTracking()
                .Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductId))
                .ToListAsync(ct);
            return stocks.Select(ToLevel).ToList();
        }, cancellationToken);

    public Task<WarehouseSummary> GetDefaultWarehouseAsync(CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct => ToSummary(await EnsureDefaultWarehouseAsync(db, tenant, clock, ct)), cancellationToken);

    public Task<InventoryMovementResult> RegisterAsync(InventoryMovementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        return db.InTenantTransactionAsync(async ct =>
        {
            var tenantId = tenant.RequireTenantId();

            // Dos solicitudes con la misma clave esperan una a la otra; la segunda encuentra el movimiento ya guardado.
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString("D") + "|" + request.IdempotencyKey}, 0))", ct);

            var existing = await db.InventoryMovements.AsNoTracking().Include(m => m.Lines)
                .SingleOrDefaultAsync(m => m.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
            {
                return existing.HasSameContentAs(request)
                    ? new InventoryMovementResult(existing.Id, true, existing.TotalCost)
                    : throw new ConflictException("inventory_idempotency_conflict",
                        "Ya existe un movimiento con esta clave y otros datos; no se sobrescribe.");
            }

            if (!await db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId && w.IsActive, ct))
            {
                throw new DomainException("inventory_warehouse_not_available", "La bodega no existe, no está activa o es de otra empresa.");
            }

            await EnsureProductsAsync(request, ct);
            var balances = await LockBalancesAsync(tenantId, request, ct);

            var movement = InventoryMovement.Create(Guid.CreateVersion7(), tenantId, request, clock.GetUtcNow(), tenant.UserId);
            db.InventoryMovements.Add(movement);
            for (var i = 0; i < request.Lines.Count; i++)
            {
                var line = request.Lines[i];
                var balance = balances[line.ProductId];
                var change = request.Kind == InventoryMovementKind.Entrada
                    ? balance.Receive(line.Quantity, line.UnitCost!.Value)
                    : balance.Issue(line.Quantity);
                movement.AddLine(InventoryMovementLineEntity.Create(tenantId, movement.Id, i + 1, line.ProductId, line.Quantity, change));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
            {
                // Respaldo: la restricción única de (tenant_id, idempotency_key) gana aunque falle el bloqueo previo.
                throw new ConflictException("inventory_idempotency_conflict", "Otra solicitud registró esta clave al mismo tiempo; repita la operación.");
            }

            return new InventoryMovementResult(movement.Id, false, movement.TotalCost);
        }, cancellationToken);
    }

    /// <summary>Bodega principal de la empresa; la crea si no existe (también la usan los listados de la API).</summary>
    internal static async Task<Warehouse> EnsureDefaultWarehouseAsync(InventoryDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var current = await db.Warehouses.SingleOrDefaultAsync(w => w.IsDefault, ct);
        if (current is not null)
        {
            return current;
        }

        // ON CONFLICT: si dos solicitudes la crean a la vez, queda una sola (índice único parcial por empresa).
        var created = Warehouse.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), Warehouse.DefaultCode, Warehouse.DefaultName,
            isDefault: true, clock.GetUtcNow(), tenant.UserId);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory.warehouses (id, tenant_id, code, name, is_default, is_active, created_at_utc, created_by)
            VALUES ({created.Id}, {created.TenantId}, {created.Code}, {created.Name}, TRUE, TRUE, {created.CreatedAtUtc}, {created.CreatedBy})
            ON CONFLICT DO NOTHING
            """, ct);

        return await db.Warehouses.SingleOrDefaultAsync(w => w.IsDefault, ct)
            ?? throw new ConflictException("inventory_default_warehouse_code_taken",
                $"No se pudo crear la bodega principal: el código {Warehouse.DefaultCode} ya lo usa otra bodega.");
    }

    internal static WarehouseSummary ToSummary(Warehouse w) => new(w.Id, w.Code, w.Name, w.IsDefault, w.IsActive);

    private static StockLevel ToLevel(StockBalance s) => new(s.ProductId, s.WarehouseId, s.QuantityOnHand, s.QuantityReserved);

    /// <summary>Reglas que no necesitan la base: se revisan antes de abrir la transacción.</summary>
    private static void Validate(InventoryMovementRequest request)
    {
        if (request.Kind is InventoryMovementKind.Ajuste or InventoryMovementKind.Transferencia || !Enum.IsDefined(request.Kind))
        {
            throw new DomainException("inventory_movement_kind_not_supported", "Por ahora solo se registran entradas y salidas.");
        }

        if (request.SourceModule is null || !Sources.TryGetValue(request.SourceModule, out var source)
            || request.SourceDocumentType != source.DocumentType || request.Kind != source.Kind
            || request.SourceDocumentId == Guid.Empty
            || request.IdempotencyKey != $"{request.SourceModule}:{request.SourceDocumentType}:{request.SourceDocumentId:D}")
        {
            throw new DomainException("inventory_source_invalid",
                "Origen no válido: compras/receipt registra entradas y sales/invoice salidas, con la clave <modulo>:<tipo>:<id del documento>.");
        }

        if (request.WarehouseId == Guid.Empty)
        {
            throw new DomainException("inventory_warehouse_not_available", "Debe indicar la bodega del movimiento.");
        }

        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(l => l is null))
        {
            throw new DomainException("inventory_lines_invalid", "El movimiento debe tener al menos una línea.");
        }

        if (request.Lines.Select(l => l.ProductId).Distinct().Count() != request.Lines.Count)
        {
            throw new DomainException("inventory_duplicate_product", "Cada artículo va una sola vez por movimiento; agrupe sus líneas.");
        }

        foreach (var line in request.Lines)
        {
            if (line.ProductId == Guid.Empty)
            {
                throw new DomainException("inventory_product_not_available", "Una línea no indica el artículo.");
            }

            if (!StockBalance.IsValidQuantity(line.Quantity))
            {
                throw new DomainException("inventory_quantity_invalid", "La cantidad debe ser mayor que cero y tener máximo 4 decimales.");
            }

            if (request.Kind == InventoryMovementKind.Salida && line.UnitCost is not null)
            {
                throw new DomainException("inventory_unit_cost_not_allowed", "En una salida el costo lo calcula Inventario; envíe UnitCost = null.");
            }

            if (request.Kind == InventoryMovementKind.Entrada)
            {
                if (line.UnitCost is not { } cost)
                {
                    throw new DomainException("inventory_unit_cost_required", "Cada línea de una entrada necesita su costo unitario.");
                }

                if (!StockBalance.IsValidUnitCost(cost))
                {
                    throw new DomainException("inventory_unit_cost_invalid", "El costo unitario no puede ser negativo y admite máximo 4 decimales.");
                }
            }
        }
    }

    private async Task EnsureProductsAsync(InventoryMovementRequest request, CancellationToken ct)
    {
        var ids = request.Lines.Select(l => l.ProductId).ToArray();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive, p.Kind, p.TracksInventory })
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var id in ids)
        {
            if (!products.TryGetValue(id, out var product))
            {
                throw new DomainException("inventory_product_not_available", $"El artículo {id} no existe en esta empresa.");
            }

            if (!product.IsActive)
            {
                throw new DomainException("inventory_product_inactive", $"El artículo {id} está inactivo.");
            }

            if (product.Kind != ProductKind.Product || !product.TracksInventory)
            {
                throw new DomainException("inventory_product_not_tracked", $"El artículo {id} no controla inventario; no se envía al movimiento.");
            }
        }
    }

    /// <summary>
    /// Bloquea (FOR UPDATE) los saldos del movimiento en orden de artículo, para evitar bloqueos cruzados entre dos
    /// documentos con los mismos artículos. Quien llega segundo espera y luego lee la existencia ya descontada.
    /// En una entrada crea antes los saldos que falten.
    /// </summary>
    private async Task<Dictionary<Guid, StockBalance>> LockBalancesAsync(Guid tenantId, InventoryMovementRequest request, CancellationToken ct)
    {
        var ids = request.Lines.Select(l => l.ProductId).Order().ToArray();
        if (request.Kind == InventoryMovementKind.Entrada)
        {
            foreach (var id in ids)
            {
                var balance = StockBalance.Create(tenantId, id, request.WarehouseId);
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO inventory.stock_balances (id, tenant_id, product_id, warehouse_id, quantity_on_hand, quantity_reserved, average_cost, last_sequence)
                    VALUES ({balance.Id}, {tenantId}, {id}, {request.WarehouseId}, 0, 0, 0, 0)
                    ON CONFLICT (tenant_id, product_id, warehouse_id) DO NOTHING
                    """, ct);
            }
        }

        // Si el contexto ya seguía alguno de estos saldos, se descarta para leer el valor bloqueado de la base.
        foreach (var entry in db.ChangeTracker.Entries<StockBalance>().Where(e => ids.Contains(e.Entity.ProductId)).ToList())
        {
            entry.State = EntityState.Detached;
        }

        var locked = await db.StockBalances
            .FromSql($"""
                SELECT * FROM inventory.stock_balances
                WHERE warehouse_id = {request.WarehouseId} AND product_id = ANY({ids})
                ORDER BY product_id
                FOR UPDATE
                """)
            .ToDictionaryAsync(s => s.ProductId, ct);

        foreach (var id in ids.Where(id => !locked.ContainsKey(id)))
        {
            // Solo pasa en una salida: sin saldo no hay existencia.
            throw new DomainException("inventory_insufficient_stock", $"No hay existencia del artículo {id} en la bodega.");
        }

        return locked;
    }
}
