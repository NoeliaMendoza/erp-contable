using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Bodegas de la empresa. La bodega principal se crea sola y no se desactiva.</summary>
public sealed class WarehouseService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<IReadOnlyList<WarehouseResponse>> ListAsync(CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync<IReadOnlyList<WarehouseResponse>>(async ct =>
        {
            await InventoryStockService.EnsureDefaultWarehouseAsync(db, tenant, clock, ct);
            return await db.Warehouses.AsNoTracking()
                .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Code)
                .Select(w => new WarehouseResponse(w.Id, w.Code, w.Name, w.IsDefault, w.IsActive))
                .ToListAsync(ct);
        }, cancellationToken);

    public Task<WarehouseResponse> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            // Primero la principal, para que la nueva nunca ocupe su lugar.
            await InventoryStockService.EnsureDefaultWarehouseAsync(db, tenant, clock, ct);
            var warehouse = Warehouse.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), request.Code, request.Name,
                isDefault: false, clock.GetUtcNow(), tenant.UserId);
            if (await db.Warehouses.AnyAsync(w => w.Code == warehouse.Code, ct))
            {
                throw Duplicated(warehouse.Code);
            }

            db.Warehouses.Add(warehouse);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
            {
                throw Duplicated(warehouse.Code);
            }

            return ToResponse(warehouse);
        }, cancellationToken);

    public Task<WarehouseResponse> RenameAsync(Guid id, RenameWarehouseRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var warehouse = await FindAsync(id, ct);
            warehouse.Rename(request.Name, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(warehouse);
        }, cancellationToken);

    public Task<WarehouseResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var warehouse = await FindAsync(id, ct);
            warehouse.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(warehouse);
        }, cancellationToken);

    internal static WarehouseResponse ToResponse(Warehouse w) => new(w.Id, w.Code, w.Name, w.IsDefault, w.IsActive);

    private async Task<Warehouse> FindAsync(Guid id, CancellationToken ct) =>
        await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Bodega", id);

    private static ConflictException Duplicated(string code) =>
        new("inventory_warehouse_code_duplicated", $"Ya existe una bodega con el código {code}.");
}
