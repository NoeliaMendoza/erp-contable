using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Categorías del catálogo. Nombre único por empresa sin distinguir mayúsculas.</summary>
public sealed class CategoryService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<PagedResult<CategoryResponse>> ListAsync(PageRequest page, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var query = db.Categories.AsNoTracking();
            if (isActive is not null)
            {
                query = query.Where(c => c.IsActive == isActive);
            }

            if (page.Term is { } term)
            {
                var pattern = SearchPattern.Contains(term);
                query = query.Where(c => EF.Functions.ILike(c.Name, pattern, "\\"));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
                .Skip(page.Skip).Take(page.SafePageSize)
                .Select(c => new CategoryResponse(c.Id, c.Name, c.IsActive))
                .ToListAsync(ct);
            return new PagedResult<CategoryResponse>(items, total, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<CategoryResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct => ToResponse(await FindAsync(id, ct)), cancellationToken);

    public Task<CategoryResponse> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var category = Category.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), request.Name, clock.GetUtcNow(), tenant.UserId);
            await EnsureNameAvailableAsync(category.Name, null, ct);
            db.Categories.Add(category);
            await SaveAsync(category.Name, ct);
            return ToResponse(category);
        }, cancellationToken);

    public Task<CategoryResponse> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var category = await FindAsync(id, ct);
            var name = Category.NormalizeName(request.Name);
            await EnsureNameAvailableAsync(name, id, ct);
            category.Rename(name, clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(name, ct);
            return ToResponse(category);
        }, cancellationToken);

    /// <summary>Igual que las unidades: no se desactiva una categoría que usan artículos ACTIVOS (409 category_in_use).</summary>
    public Task<CategoryResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var category = await FindAsync(id, ct);
            if (!isActive && category.IsActive)
            {
                var inUse = await db.Products.CountAsync(p => p.CategoryId == id && p.IsActive, ct);
                if (inUse > 0)
                {
                    throw new ConflictException("category_in_use",
                        $"La categoría {category.Name} la usan {inUse} artículo(s) activo(s). Desactívelos o cámbieles la categoría primero.");
                }
            }

            category.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(category);
        }, cancellationToken);

    private async Task<Category> FindAsync(Guid id, CancellationToken ct) =>
        await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Categoría", id);

    private async Task EnsureNameAvailableAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        // Se traduce a SQL upper(name), la misma expresión del índice único de la base (string.Equals no se traduce).
        var upper = name.ToUpperInvariant();
#pragma warning disable CA1862
        var exists = await db.Categories.AnyAsync(c => c.Name.ToUpper() == upper && c.Id != exceptId, ct);
#pragma warning restore CA1862
        if (exists)
        {
            throw Duplicate(name);
        }
    }

    private async Task SaveAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            throw Duplicate(name);
        }
    }

    private static ConflictException Duplicate(string name) =>
        new("category_name_duplicated", $"Ya existe una categoría llamada '{name}' en esta empresa.");

    private static CategoryResponse ToResponse(Category c) => new(c.Id, c.Name, c.IsActive);
}
