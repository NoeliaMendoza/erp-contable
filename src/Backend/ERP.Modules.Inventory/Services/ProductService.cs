using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using ERP.Modules.Inventory.Validators;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>Casos de uso del catálogo de productos y servicios. No calcula existencias.</summary>
public sealed class ProductService(InventoryDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private const string BarcodeIndex = "ix_products_tenant_id_barcode";

    public Task<PagedResult<ProductResponse>> ListAsync(
        PageRequest page, ProductKind? kind, bool? isActive, Guid? categoryId, CancellationToken cancellationToken) =>
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

            if (categoryId is not null)
            {
                query = query.Where(p => p.CategoryId == categoryId);
            }

            if (page.Term is { } term)
            {
                // También por código de barras: al escanear con lector se encuentra el artículo directamente.
                var pattern = SearchPattern.Contains(term);
                query = query.Where(p =>
                    EF.Functions.ILike(p.Sku, pattern, "\\")
                    || EF.Functions.ILike(p.Name, pattern, "\\")
                    || (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern, "\\")));
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
            var category = await RequireAssignableCategoryAsync(request.CategoryId, currentCategoryId: null, ct);
            var product = Product.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), ToData(request), clock.GetUtcNow(), tenant.UserId);
            await EnsureCodesAvailableAsync(product.Sku, product.Barcode, null, ct);
            db.Products.Add(product);
            await SaveAsync(product, ct);
            return ToResponse(product, unit, category);
        }, cancellationToken);

    public Task<ProductResponse> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var product = await FindAsync(id, ct);
            var unit = await RequireAssignableUnitAsync(request.UnitId, product.UnitId, ct);
            var category = await RequireAssignableCategoryAsync(request.CategoryId, product.CategoryId, ct);
            await EnsureCodesAvailableAsync(Product.NormalizeSku(request.Sku), Product.NormalizeBarcode(request.Barcode), id, ct);
            product.Update(ToData(request), clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(product, ct);
            return ToResponse(product, unit, category);
        }, cancellationToken);

    /// <summary>Baja lógica. Reactivar exige que su unidad (y su categoría, si tiene) estén activas.</summary>
    public Task<ProductResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var product = await FindAsync(id, ct);
            var unit = await db.Units.SingleAsync(u => u.Id == product.UnitId, ct);
            var category = product.CategoryId is { } categoryId
                ? await db.Categories.SingleAsync(c => c.Id == categoryId, ct)
                : null;

            if (isActive && !unit.IsActive)
            {
                throw new DomainException("unit_inactive",
                    $"No se puede activar: la unidad {unit.Code} está inactiva. Active la unidad o cambie la del artículo.");
            }

            if (isActive && category is { IsActive: false })
            {
                throw new DomainException("category_inactive",
                    $"No se puede activar: la categoría {category.Name} está inactiva. Actívela o cambie la del artículo.");
            }

            product.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(product, unit, category);
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

    /// <summary>Misma política que la unidad, pero la categoría es opcional.</summary>
    private async Task<Category?> RequireAssignableCategoryAsync(Guid? categoryId, Guid? currentCategoryId, CancellationToken ct)
    {
        if (categoryId is null || categoryId == Guid.Empty)
        {
            return null;
        }

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new DomainException("category_not_available", "La categoría seleccionada no existe en esta empresa.");

        if (!category.IsActive && category.Id != currentCategoryId)
        {
            throw new DomainException("category_inactive", $"La categoría {category.Name} está inactiva. Elija una categoría activa.");
        }

        return category;
    }

    private async Task<Product> FindAsync(Guid id, CancellationToken ct) =>
        await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Artículo", id);

    private async Task EnsureCodesAvailableAsync(string sku, string? barcode, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != exceptId, ct))
        {
            throw DuplicateSku(sku);
        }

        if (barcode is not null && await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != exceptId, ct))
        {
            throw DuplicateBarcode(barcode);
        }
    }

    private async Task SaveAsync(Product product, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            throw UniqueViolation.Constraint(ex) == BarcodeIndex && product.Barcode is not null
                ? DuplicateBarcode(product.Barcode)
                : DuplicateSku(product.Sku);
        }
    }

    private static ProductData ToData(SaveProductRequest r) =>
        new(r.Sku, r.Name, ParseKind(r.Kind), r.UnitId, r.Price, ParseVat(r.VatTreatment),
            r.CategoryId, r.Barcode, r.Description, r.Cost, r.TracksInventory, r.MinStock);

    private static ProductKind ParseKind(string kind) =>
        KindParser.TryParse(kind, out var parsed)
            ? parsed
            : throw new DomainException("product_kind_invalid", "El tipo debe ser Product (producto) o Service (servicio).");

    private static VatTreatment ParseVat(string? value) =>
        VatTreatmentParser.TryParse(value, out var parsed)
            ? parsed
            : throw new DomainException("product_vat_invalid", "Seleccione el tratamiento de IVA del artículo.");

    private static ConflictException DuplicateSku(string sku) =>
        new("product_sku_duplicated", $"Ya existe un artículo con el SKU '{sku}' en esta empresa.");

    private static ConflictException DuplicateBarcode(string barcode) =>
        new("product_barcode_duplicated", $"Ya existe un artículo con el código de barras '{barcode}' en esta empresa.");

    private IQueryable<ProductResponse> Project(IQueryable<Product> products) =>
        products.Join(db.Units, p => p.UnitId, u => u.Id, (p, u) => new ProductResponse(
            p.Id, p.Sku, p.Name, p.Kind.ToString(), p.UnitId, u.Code, u.Name, p.Price, p.IsActive,
            p.VatTreatment.ToString(), p.CategoryId,
            db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
            p.Barcode, p.Description, p.Cost, p.TracksInventory, p.MinStock));

    private static ProductResponse ToResponse(Product p, Unit u, Category? c) =>
        new(p.Id, p.Sku, p.Name, p.Kind.ToString(), p.UnitId, u.Code, u.Name, p.Price, p.IsActive,
            p.VatTreatment.ToString(), p.CategoryId, c?.Name, p.Barcode, p.Description, p.Cost, p.TracksInventory, p.MinStock);
}
