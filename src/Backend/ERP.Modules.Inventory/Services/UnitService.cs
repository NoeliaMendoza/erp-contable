using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Casos de uso de unidades de medida. Las lecturas ocurren dentro de una transacción (contexto RLS).</summary>
public sealed class UnitService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<PagedResult<UnitResponse>> ListAsync(PageRequest page, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var query = db.Units.AsNoTracking();
            if (isActive is not null)
            {
                query = query.Where(u => u.IsActive == isActive);
            }

            if (page.Term is { } term)
            {
                var pattern = SearchPattern.Contains(term);
                query = query.Where(u => EF.Functions.ILike(u.Code, pattern, "\\") || EF.Functions.ILike(u.Name, pattern, "\\"));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(u => u.Code).ThenBy(u => u.Id)
                .Skip(page.Skip).Take(page.SafePageSize)
                .Select(u => new UnitResponse(u.Id, u.Code, u.Name, u.IsActive))
                .ToListAsync(ct);
            return new PagedResult<UnitResponse>(items, total, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<UnitResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct => ToResponse(await FindAsync(id, ct)), cancellationToken);

    public Task<UnitResponse> CreateAsync(SaveUnitRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var unit = Unit.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), request.Code, request.Name, clock.GetUtcNow(), tenant.UserId);
            await EnsureCodeAvailableAsync(unit.Code, null, ct);
            db.Units.Add(unit);
            await SaveAsync(unit.Code, ct);
            return ToResponse(unit);
        }, cancellationToken);

    public Task<UnitResponse> UpdateAsync(Guid id, SaveUnitRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var unit = await FindAsync(id, ct);
            var code = Unit.NormalizeCode(request.Code);
            await EnsureCodeAvailableAsync(code, id, ct);
            unit.Update(code, request.Name, clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(code, ct);
            return ToResponse(unit);
        }, cancellationToken);

    /// <summary>
    /// Política: no se puede desactivar una unidad que usan productos ACTIVOS (409 unit_in_use),
    /// para no dejar artículos vendibles con una unidad inválida. Primero se desactivan o cambian esos artículos.
    /// </summary>
    public Task<UnitResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var unit = await FindAsync(id, ct);
            if (!isActive && unit.IsActive)
            {
                var inUse = await db.Products.CountAsync(p => p.UnitId == id && p.IsActive, ct);
                if (inUse > 0)
                {
                    throw new ConflictException("unit_in_use",
                        $"La unidad {unit.Code} la usan {inUse} artículo(s) activo(s). Desactívelos o cámbieles la unidad primero.");
                }
            }

            unit.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(unit);
        }, cancellationToken);

    private async Task<Unit> FindAsync(Guid id, CancellationToken ct) =>
        await db.Units.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Unidad", id);

    private async Task EnsureCodeAvailableAsync(string code, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Units.AnyAsync(u => u.Code == code && u.Id != exceptId, ct))
        {
            throw DuplicateCode(code);
        }
    }

    private async Task SaveAsync(string code, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            throw DuplicateCode(code);
        }
    }

    private static ConflictException DuplicateCode(string code) =>
        new("unit_code_duplicated", $"Ya existe una unidad con el código '{code}' en esta empresa.");

    private static UnitResponse ToResponse(Unit u) => new(u.Id, u.Code, u.Name, u.IsActive);
}
