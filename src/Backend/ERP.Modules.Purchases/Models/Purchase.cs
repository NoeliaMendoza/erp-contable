using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Inventory.Contracts;

namespace ERP.Modules.Purchases.Models;

public enum PurchaseStatus
{
    Draft = 1,
    Confirmed = 2,
}

/// <summary>Compra al contado de una empresa. Una compra confirmada es inmutable.</summary>
public sealed class Purchase : ITenantOwned
{
    private readonly List<PurchaseLine> _lines = [];

    private Purchase() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public string SupplierName { get; private set; } = string.Empty;
    public DateOnly PurchaseDate { get; private set; }
    public Guid WarehouseId { get; private set; }
    public PurchaseStatus Status { get; private set; }
    public decimal TotalCost { get; private set; }
    public Guid? StockMovementId { get; private set; }
    public Guid? JournalEntryId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public IReadOnlyList<PurchaseLine> Lines => _lines;

    public string ReceiptIdempotencyKey => $"purchases:receipt:{Id:D}";

    public static Purchase Create(Guid id, Guid tenantId, string number, Guid supplierId, string supplierName,
        DateOnly purchaseDate, Guid warehouseId, IReadOnlyCollection<PurchaseLineData> lines,
        DateTimeOffset nowUtc, Guid? userId)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || supplierId == Guid.Empty || warehouseId == Guid.Empty)
            throw new DomainException("purchase_identity_invalid", "La compra, empresa, proveedor y bodega deben ser válidos.");
        if (string.IsNullOrWhiteSpace(number) || number.Trim().Length > 30)
            throw new DomainException("purchase_number_invalid", "El número de compra es obligatorio y admite máximo 30 caracteres.");
        if (string.IsNullOrWhiteSpace(supplierName) || supplierName.Trim().Length > 200)
            throw new DomainException("purchase_supplier_invalid", "El proveedor debe tener un nombre válido.");
        if (lines is null || lines.Count == 0 || lines.Any(line => line is null))
            throw new DomainException("purchase_lines_invalid", "La compra debe incluir al menos un artículo.");
        if (lines.Select(line => line.Product.ProductId).Distinct().Count() != lines.Count)
            throw new DomainException("purchase_duplicate_product", "Cada artículo debe aparecer una sola vez en la compra.");

        var purchase = new Purchase
        {
            Id = id,
            TenantId = tenantId,
            Number = number.Trim(),
            SupplierId = supplierId,
            SupplierName = supplierName.Trim(),
            PurchaseDate = purchaseDate,
            WarehouseId = warehouseId,
            Status = PurchaseStatus.Draft,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };

        foreach (var line in lines)
        {
            if (!line.Product.IsActive || !line.Product.TracksInventory)
                throw new DomainException("purchase_product_unavailable", "Solo se pueden comprar artículos activos que controlan inventario.");

            var purchaseLine = PurchaseLine.Create(tenantId, id, line.Product, line.Quantity, line.UnitCost);
            purchase._lines.Add(purchaseLine);
            purchase.TotalCost += purchaseLine.LineCost;
        }

        purchase.TotalCost = decimal.Round(purchase.TotalCost, 2, MidpointRounding.AwayFromZero);
        return purchase;
    }

    public void Confirm(Guid stockMovementId, Guid journalEntryId)
    {
        if (Status == PurchaseStatus.Confirmed)
        {
            if (StockMovementId != stockMovementId || JournalEntryId != journalEntryId)
                throw new ConflictException("purchase_confirmation_conflict", "La compra ya fue confirmada con otros movimientos.");
            return;
        }

        if (_lines.Count == 0 || stockMovementId == Guid.Empty || journalEntryId == Guid.Empty)
            throw new DomainException("purchase_confirmation_invalid", "La compra requiere líneas, movimiento de inventario y asiento contable.");

        StockMovementId = stockMovementId;
        JournalEntryId = journalEntryId;
        Status = PurchaseStatus.Confirmed;
    }
}

public sealed class PurchaseLine
{
    private PurchaseLine() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PurchaseId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string UnitCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineCost { get; private set; }

    internal static PurchaseLine Create(Guid tenantId, Guid purchaseId, ProductSummary product, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0 || decimal.Round(quantity, 4) != quantity)
            throw new DomainException("purchase_quantity_invalid", "La cantidad debe ser mayor que cero y admite máximo 4 decimales.");
        if (unitCost < 0 || decimal.Round(unitCost, 4) != unitCost)
            throw new DomainException("purchase_unit_cost_invalid", "El costo unitario no puede ser negativo y admite máximo 4 decimales.");

        return new PurchaseLine
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            PurchaseId = purchaseId,
            ProductId = product.ProductId,
            Code = product.Code,
            Name = product.Name,
            UnitCode = product.UnitCode,
            Quantity = quantity,
            UnitCost = unitCost,
            LineCost = decimal.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero),
        };
    }
}

public sealed record PurchaseLineData(ProductSummary Product, decimal Quantity, decimal UnitCost);
