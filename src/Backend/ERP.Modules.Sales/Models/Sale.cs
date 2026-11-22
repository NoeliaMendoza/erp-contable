using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Inventory.Contracts;

namespace ERP.Modules.Sales.Models;

public enum SaleStatus { Draft = 1, Confirmed = 2 }

public sealed class Sale : ITenantOwned
{
    private readonly List<SaleLine> _lines = [];
    private Sale() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public DateOnly SaleDate { get; private set; }
    public SaleStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? JournalEntryId { get; private set; }
    public Guid? StockMovementId { get; private set; }
    public string ElectronicStatus { get; private set; } = "NotRequested";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public IReadOnlyList<SaleLine> Lines => _lines;

    internal void AddLine(SaleLine line) => _lines.Add(line);

    public static Sale Create(Guid id, Guid tenantId, string number, string customerName, Guid? customerId,
        DateOnly saleDate, string idempotencyKey, DateTimeOffset nowUtc, Guid? userId)
    {
        var name = customerName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
            throw new DomainException("sale_customer_invalid", "El nombre del cliente es obligatorio y admite máximo 200 caracteres.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("sale_idempotency_required", "La operación requiere una clave de idempotencia.");

        return new Sale
        {
            Id = id,
            TenantId = tenantId,
            Number = number,
            CustomerName = name,
            CustomerId = customerId,
            SaleDate = saleDate,
            Status = SaleStatus.Draft,
            IdempotencyKey = idempotencyKey.Trim(),
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };
    }

    public void SetTotals(decimal subtotal, decimal taxTotal)
    {
        if (Status != SaleStatus.Draft)
            throw new DomainException("sale_confirmed", "Una venta confirmada no puede modificarse.");
        Subtotal = decimal.Round(subtotal, 2);
        TaxTotal = decimal.Round(taxTotal, 2);
        Total = decimal.Round(Subtotal + TaxTotal, 2);
    }

    public void Confirm(Guid stockMovementId, Guid journalEntryId)
    {
        if (Status == SaleStatus.Confirmed) return;
        if (_lines.Count == 0)
            throw new DomainException("sale_no_lines", "La venta debe tener al menos un producto o servicio.");
        Status = SaleStatus.Confirmed;
        StockMovementId = stockMovementId;
        JournalEntryId = journalEntryId;
    }
}

public sealed class SaleLine
{
    private SaleLine() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Discount { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    public static SaleLine Create(Guid tenantId, Guid saleId, ProductSummary product, decimal quantity, decimal unitPrice,
        decimal discount, decimal taxRate)
    {
        if (quantity <= 0 || decimal.Round(quantity, 4) != quantity)
            throw new DomainException("sale_quantity_invalid", "La cantidad debe ser mayor que cero y tener máximo 4 decimales.");
        if (unitPrice < 0 || decimal.Round(unitPrice, 4) != unitPrice)
            throw new DomainException("sale_price_invalid", "El precio no puede ser negativo y admite máximo 4 decimales.");
        if (discount < 0 || decimal.Round(discount, 2) != discount)
            throw new DomainException("sale_discount_invalid", "El descuento no puede ser negativo y admite máximo 2 decimales.");
        var baseAmount = decimal.Round(quantity * unitPrice - discount, 2);
        if (baseAmount < 0)
            throw new DomainException("sale_discount_too_high", "El descuento no puede superar el importe de la línea.");
        var taxAmount = decimal.Round(baseAmount * taxRate / 100m, 2);
        return new SaleLine
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SaleId = saleId,
            ProductId = product.ProductId,
            Sku = product.Code,
            Name = product.Name,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Discount = discount,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            LineTotal = baseAmount + taxAmount,
        };
    }
}
