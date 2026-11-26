using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Purchases.Data;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Purchases.Services;

/// <summary>Registro y consulta tenant-scoped de compras contado.</summary>
public sealed class PurchaseService(
    PurchasesDbContext db,
    IProductCatalog products,
    IStockQueryService stock,
    ITenantContext tenant,
    TimeProvider clock)
{
    public Task<PurchaseResponse> RegisterAsync(RegisterPurchaseRequest request, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            if (request.SupplierId == Guid.Empty)
                throw new DomainException("purchase_supplier_required", "Debe seleccionar un proveedor.");

            var supplier = await db.Suppliers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.SupplierId && x.IsActive, token)
                ?? throw new DomainException("purchase_supplier_unavailable", "El proveedor no existe, está inactivo o pertenece a otra empresa.");

            if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(line => line is null))
                throw new DomainException("purchase_lines_invalid", "La compra debe incluir al menos un artículo.");

            var productIds = request.Lines.Select(line => line.ProductId).Distinct().ToArray();
            if (productIds.Any(id => id == Guid.Empty) || productIds.Length != request.Lines.Count)
                throw new DomainException("purchase_product_ids_invalid", "Los artículos deben ser válidos y no repetirse.");

            var catalog = await products.GetByIdsAsync(productIds, token);
            var productById = catalog.ToDictionary(product => product.ProductId);
            if (productById.Count != productIds.Length)
                throw new DomainException("purchase_product_unavailable", "Uno o más artículos no existen en esta empresa.");

            foreach (var product in productById.Values)
            {
                if (!product.IsActive || !product.TracksInventory)
                    throw new DomainException("purchase_product_unavailable", $"El artículo '{product.Name}' no está activo o no controla inventario.");
            }

            var warehouse = await stock.GetDefaultWarehouseAsync(token);
            if (warehouse.WarehouseId == Guid.Empty || !warehouse.IsActive)
                throw new DomainException("purchase_warehouse_unavailable", "La bodega no existe o está inactiva.");

            var purchaseDate = request.PurchaseDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var id = Guid.CreateVersion7();
            var purchase = Purchase.Create(id, tenant.RequireTenantId(), $"C-{id:N}"[..30], supplier.Id, supplier.Name,
                purchaseDate, warehouse.WarehouseId,
                request.Lines.Select(line => new PurchaseLineData(productById[line.ProductId], line.Quantity, line.UnitCost)).ToArray(),
                clock.GetUtcNow(), tenant.UserId);

            db.Purchases.Add(purchase);
            await db.SaveChangesAsync(token);
            return ToResponse(purchase);
        }, ct);

    public Task<PagedResult<PurchaseResponse>> ListAsync(PageRequest page, string? status, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var query = db.Purchases.AsNoTracking().Include(x => x.Lines).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<PurchaseStatus>(status, true, out var parsedStatus))
                query = query.Where(x => x.Status == parsedStatus);
            else if (!string.IsNullOrWhiteSpace(status))
                throw new DomainException("purchase_status_invalid", "El estado de compra no es válido.");

            if (page.Term is { } term)
            {
                var pattern = Contains(term);
                query = query.Where(x => EF.Functions.ILike(x.Number, pattern, "\\")
                    || EF.Functions.ILike(x.SupplierName, pattern, "\\"));
            }

            var total = await query.CountAsync(token);
            var items = await query.OrderByDescending(x => x.PurchaseDate).ThenByDescending(x => x.Id)
                .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(token);
            return new PagedResult<PurchaseResponse>(items.Select(ToResponse).ToList(), total,
                page.SafePage, page.SafePageSize);
        }, ct);

    public Task<PurchaseResponse> GetAsync(Guid id, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var purchase = await db.Purchases.AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("Compra", id);
            return ToResponse(purchase);
        }, ct);

    private static PurchaseResponse ToResponse(Purchase purchase) => new(
        purchase.Id, purchase.Number, purchase.SupplierId, purchase.SupplierName, purchase.PurchaseDate,
        purchase.WarehouseId, purchase.Status.ToString(), purchase.TotalCost, purchase.StockMovementId,
        purchase.JournalEntryId, purchase.Lines.Select(line => new PurchaseLineResponse(line.Id,
            line.ProductId, line.Code, line.Name, line.UnitCode, line.Quantity, line.UnitCost, line.LineCost)).ToArray());

    private static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
