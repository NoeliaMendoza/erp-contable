using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Purchases.Data;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Modules.Purchases.Services;

/// <summary>Registro, consulta y confirmación de compras al contado de la empresa activa.</summary>
public sealed class PurchaseService(
    PurchasesDbContext db,
    IProductCatalog products,
    IStockQueryService stock,
    ITenantContext tenant,
    TimeProvider clock,
    IInventoryMovementService inventory,
    IAccountingPostingService accounting,
    SharedTenantTransactionContext sharedTransaction,
    IConfiguration configuration)
{
    private static readonly TimeZoneInfo EcuadorTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");

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

            var now = clock.GetUtcNow();
            var purchaseDate = request.PurchaseDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, EcuadorTimeZone).DateTime);
            var id = Guid.CreateVersion7();
            var purchase = Purchase.Create(id, tenant.RequireTenantId(), $"C-{id:N}"[..30], supplier.Id, supplier.Name,
                purchaseDate, warehouse.WarehouseId,
                request.Lines.Select(line => new PurchaseLineData(productById[line.ProductId], line.Quantity, line.UnitCost)).ToArray(),
                now, tenant.UserId);

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

    public Task<PurchaseResponse> ConfirmAsync(Guid id, CancellationToken ct) =>
        db.InSharedTenantTransactionAsync(sharedTransaction, async token =>
        {
            var tenantId = tenant.RequireTenantId();
            // El bloqueo se toma antes de leer el estado y dura hasta el commit compartido.
            var lockKey = $"{tenantId:D}|purchases:receipt:{id:D}";
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", token);
            var purchase = await db.Purchases.Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("Compra", id);
            if (purchase.Status == PurchaseStatus.Confirmed)
                return ToResponse(purchase);

            var accounts = configuration.GetSection($"Purchases:PostingAccounts:{tenantId:D}");
            var inventoryAccount = accounts["Inventory"]?.Trim().ToUpperInvariant();
            var cashAccount = accounts["Cash"]?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(inventoryAccount) || string.IsNullOrEmpty(cashAccount) || inventoryAccount == cashAccount)
                throw new DomainException("purchase_posting_accounts_invalid", "Configure cuentas distintas de Inventario y Caja para la empresa.");

            var movement = await inventory.RegisterAsync(new InventoryMovementRequest(
                purchase.ReceiptIdempotencyKey, PurchasesModule.Module, "receipt", purchase.Id,
                purchase.WarehouseId, InventoryMovementKind.Entrada, purchase.PurchaseDate,
                purchase.Lines.Select(line => new InventoryMovementLine(line.ProductId, line.Quantity, line.UnitCost)).ToArray()), token);
            if (movement.TotalCost <= 0m)
                throw new DomainException("purchase_total_cost_invalid", "El costo total de la recepción debe ser mayor que cero para contabilizarla.");

            var posting = await accounting.PostAsync(new PostingRequest(
                purchase.ReceiptIdempotencyKey, PurchasesModule.Module, "receipt", purchase.Id,
                purchase.PurchaseDate, $"Compra al contado {purchase.Number}",
                [
                    new PostingLine(inventoryAccount, movement.TotalCost, 0m, "Entrada de inventario"),
                    new PostingLine(cashAccount, 0m, movement.TotalCost, "Compra al contado"),
                ]), token);
            purchase.Confirm(movement.MovementId, posting.JournalEntryId);
            await db.SaveChangesAsync(token);
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
