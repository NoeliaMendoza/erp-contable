using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Sales.Data;
using ERP.Modules.Sales.Dtos;
using ERP.Modules.Sales.Models;
using ERP.Tributario.Tax;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Sales.Services;

public sealed class SaleService(
    SalesDbContext db,
    IProductCatalog products,
    IInventoryMovementService inventory,
    IAccountingPostingService accounting,
    ITaxRateCatalogProvider taxRates,
    ITenantContext tenant,
    TimeProvider clock)
{
    public async Task<SaleResponse> ConfirmAsync(ConfirmSaleRequest request, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        if (request.Lines.Count == 0)
            throw new DomainException("sale_no_lines", "La venta debe tener al menos una línea.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new DomainException("sale_idempotency_required", "La operación requiere una clave de idempotencia.");

        return await db.InTenantTransactionAsync(async transactionCt =>
        {
            var existing = await db.Sales.Include(s => s.Lines)
                .SingleOrDefaultAsync(s => s.IdempotencyKey == request.IdempotencyKey.Trim(), transactionCt);
            if (existing is not null)
                return ToResponse(existing);

            var ids = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
            var catalog = await products.GetByIdsAsync(ids, transactionCt);
            var catalogById = catalog.ToDictionary(p => p.ProductId);
            if (catalogById.Count != ids.Length)
                throw new DomainException("sale_article_unavailable", "Uno o más artículos no existen o no están activos en esta empresa.");

            var saleDate = request.SaleDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var sale = Sale.Create(Guid.CreateVersion7(), tenantId, $"V-{Guid.CreateVersion7():N}".Substring(0, 30),
                request.CustomerName, request.CustomerId, saleDate, request.IdempotencyKey, clock.GetUtcNow(), tenant.UserId);
            db.Sales.Add(sale);

            var taxCatalog = await taxRates.GetCatalogAsync(transactionCt);
            decimal subtotal = 0m;
            decimal taxTotal = 0m;
            foreach (var lineRequest in request.Lines)
            {
                var product = catalogById[lineRequest.ProductId];
                if (!product.IsActive)
                    throw new DomainException("sale_article_inactive", $"El artículo '{product.Name}' está inactivo.");

                var taxCode = product.VatTreatment switch
                {
                    VatTreatment.Standard => "IVA-GENERAL",
                    _ => "IVA-0",
                };
                var taxRate = taxCatalog.Resolve(TaxKind.Iva, taxCode, saleDate).Percentage;
                var price = product.Price;
                var line = SaleLine.Create(tenantId, sale.Id, product, lineRequest.Quantity, price,
                    lineRequest.Discount, taxRate);
                db.SaleLines.Add(line);
                sale.AddLine(line);
                subtotal += decimal.Round(line.Quantity * line.UnitPrice - line.Discount, 2);
                taxTotal += line.TaxAmount;
            }

            sale.SetTotals(subtotal, taxTotal);
            await db.SaveChangesAsync(transactionCt);

            // ACUERDO-01: solo artículos con control de inventario, una línea por artículo y sin costo
            // (Inventario aplica el costo promedio y lo devuelve en TotalCost).
            var inventoryLines = request.Lines
                .Where(l => catalogById[l.ProductId].TracksInventory)
                .GroupBy(l => l.ProductId)
                .Select(g => new InventoryMovementLine(g.Key, g.Sum(l => l.Quantity), null))
                .ToArray();

            InventoryMovementResult stockResult = new(Guid.Empty, true, 0m);
            if (inventoryLines.Length > 0)
            {
                stockResult = await inventory.RegisterAsync(new InventoryMovementRequest(
                    $"sales:invoice:{sale.Id:D}", "sales", "invoice", sale.Id, request.WarehouseId,
                    InventoryMovementKind.Salida, saleDate,
                    inventoryLines), transactionCt);
            }

            var postingLines = new List<PostingLine>
            {
                new("Caja", sale.Total, 0m, "Caja por venta al contado"),
                new("Ventas", 0m, subtotal, "Ingresos de venta"),
            };
            if (taxTotal > 0)
                postingLines.Add(new PostingLine("IVA por pagar", 0m, taxTotal, "IVA calculado"));

            var costOfGoodsSold = stockResult.TotalCost;
            if (costOfGoodsSold > 0)
            {
                postingLines.Add(new PostingLine("Costo de ventas", costOfGoodsSold, 0m, "Costo de mercancía vendida"));
                postingLines.Add(new PostingLine("Inventario", 0m, costOfGoodsSold, "Salida de inventario"));
            }

            var posting = await accounting.PostAsync(new PostingRequest(
                $"sales:invoice:{sale.Id}", "sales", "sale", sale.Id, saleDate,
                $"Venta al contado {sale.Number}",
                postingLines), transactionCt);

            sale.Confirm(stockResult.MovementId, posting.JournalEntryId);
            await db.SaveChangesAsync(transactionCt);
            return ToResponse(sale);
        }, ct);
    }

    public async Task<SaleResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var sale = await db.Sales.Include(s => s.Lines).AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Venta", id);
        return ToResponse(sale);
    }

    private static SaleResponse ToResponse(Sale sale) => new(
        sale.Id, sale.Number, sale.CustomerName, sale.CustomerId, sale.SaleDate, sale.Status.ToString(),
        sale.Subtotal, sale.TaxTotal, sale.Total, sale.ElectronicStatus, sale.StockMovementId, sale.JournalEntryId,
        sale.Lines.Select(l => new SaleLineResponse(l.Id, l.ProductId, l.Sku, l.Name, l.Quantity,
            l.UnitPrice, l.Discount, l.TaxRate, l.TaxAmount, l.LineTotal)).ToList());
}
