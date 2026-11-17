using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using ERP.Modules.Inventory.Validators;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Casos de uso del catálogo de productos y servicios. No calcula existencias.</summary>
public sealed class ProductService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<PagedResult<ProductResponse>> ListAsync(PageRequest page, ProductKind? kind, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var query = db.Products.AsNoTracking();
            if (kind is not null)
            {
                query = query.Where(p => p.Kind == kind);
            }

            if (isActive is not null)
            {
                query = query.Where(p => p.IsActive == isActive);
            }

            if (page.Term is { } term)
            {
                var pattern = SearchPattern.Contains(term);
                query = query.Where(p => EF.Functions.ILike(p.Sku, pattern, "\\") || EF.Functions.ILike(p.Name, pattern, "\\"));
            }

            var total = await query.CountAsync(ct);
            var items = await Project(query.OrderBy(p => p.Sku).ThenBy(p => p.Id).Skip(page.Skip).Take(page.SafePageSize))
                .ToListAsync(ct);
            return new PagedResult<ProductResponse>(items, total, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
            await Project(db.Products.AsNoTracking().Where(p => p.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Artículo", id),
            cancellationToken);

    public Task<ProductResponse> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var unit = await RequireAssignableUnitAsync(request.UnitId, currentUnitId: null, ct);
            var product = Product.Create(
                Guid.CreateVersion7(), tenant.RequireTenantId(), request.Sku, request.Name, ParseKind(request.Kind),
                unit.Id, request.Price, clock.GetUtcNow(), tenant.UserId);
            await EnsureSkuAvailableAsync(product.Sku, null, ct);
            db.Products.Add(product);
            await SaveAsync(product.Sku, ct);
            return ToResponse(product, unit);
        }, cancellationToken);

    public Task<ProductResponse> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var product = await FindAsync(id, ct);
            var unit = await RequireAssignableUnitAsync(request.UnitId, product.UnitId, ct);
            var sku = Product.NormalizeSku(request.Sku);
            await EnsureSkuAvailableAsync(sku, id, ct);
            product.Update(sku, request.Name, ParseKind(request.Kind), unit.Id, request.Price, clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(sku, ct);
            return ToResponse(product, unit);
        }, cancellationToken);

    /// <summary>Baja lógica. Reactivar exige que su unidad esté activa (no se reactivan vínculos inválidos).</summary>
    public Task<ProductResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var product = await FindAsync(id, ct);
            var unit = await db.Units.SingleAsync(u => u.Id == product.UnitId, ct);
            if (isActive && !unit.IsActive)
            {
                throw new DomainException("unit_inactive",
                    $"No se puede activar: la unidad {unit.Code} está inactiva. Active la unidad o cambie la del artículo.");
            }

            product.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(product, unit);
        }, cancellationToken);

    /// <summary>
    /// La unidad debe existir en la empresa activa (el filtro de tenant y RLS ocultan las ajenas, así que un
    /// ID de otra empresa se trata igual que uno inexistente) y estar activa. Se permite conservar la unidad
    /// actual aunque se haya desactivado después.
    /// </summary>
    private async Task<Unit> RequireAssignableUnitAsync(Guid unitId, Guid? currentUnitId, CancellationToken ct)
    {
        var unit = await db.Units.SingleOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new DomainException("unit_not_available", "La unidad seleccionada no existe en esta empresa.");

        if (!unit.IsActive && unit.Id != currentUnitId)
        {
            throw new DomainException("unit_inactive", $"La unidad {unit.Code} está inactiva. Elija una unidad activa.");
        }

        return unit;
    }

    private async Task<Product> FindAsync(Guid id, CancellationToken ct) =>
        await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Artículo", id);

    private async Task EnsureSkuAvailableAsync(string sku, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != exceptId, ct))
        {
            throw DuplicateSku(sku);
        }
    }

    private async Task SaveAsync(string sku, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            throw DuplicateSku(sku);
        }
    }

    private static ProductKind ParseKind(string kind) =>
        KindParser.TryParse(kind, out var parsed)
            ? parsed
            : throw new DomainException("product_kind_invalid", "El tipo debe ser Product (producto) o Service (servicio).");

    private static ConflictException DuplicateSku(string sku) =>
        new("product_sku_duplicated", $"Ya existe un artículo con el SKU '{sku}' en esta empresa.");

    private IQueryable<ProductResponse> Project(IQueryable<Product> products) =>
        products.Join(db.Units, p => p.UnitId, u => u.Id, (p, u) => new ProductResponse(
            p.Id, p.Sku, p.Name, p.Kind.ToString(), p.UnitId, u.Code, u.Name, p.Price, p.IsActive));

    private static ProductResponse ToResponse(Product p, Unit u) =>
        new(p.Id, p.Sku, p.Name, p.Kind.ToString(), p.UnitId, u.Code, u.Name, p.Price, p.IsActive);
}
