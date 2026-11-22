using ERP.BuildingBlocks.Domain;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Sales.Models;

namespace ERP.UnitTests.Sales;

public sealed class SaleModelTests
{
    [Fact]
    public void Cuadernos_4_a_5_totalizan_20()
    {
        var tenantId = Guid.NewGuid();
        var sale = Sale.Create(Guid.NewGuid(), tenantId, "V-001", "Cliente demo", null,
            new DateOnly(2026, 11, 19), "test-sale-1", DateTimeOffset.UtcNow, null);
        var line = SaleLine.Create(tenantId, sale.Id,
            new ProductSummary(Guid.NewGuid(), "CUAD-001", "Cuaderno", false, "UND", true,
                VatTreatment.ZeroRated, true, "7860000000017", 5m, 3m),
            4m, 5m, 0m, 0m);

        sale.AddLine(line);
        sale.SetTotals(line.Quantity * line.UnitPrice - line.Discount, line.TaxAmount);

        Assert.Equal(20m, sale.Subtotal);
        Assert.Equal(20m, sale.Total);
        Assert.Equal(0m, line.TaxAmount);
    }

    [Fact]
    public void Descuento_mayor_al_importe_es_rechazado()
    {
        var tenantId = Guid.NewGuid();
        var ex = Assert.Throws<DomainException>(() => SaleLine.Create(tenantId, Guid.NewGuid(),
            new ProductSummary(Guid.NewGuid(), "CUAD-001", "Cuaderno", false, "UND", true,
                VatTreatment.Standard, true, null, 5m, 3m),
            1m, 5m, 6m, 15m));

        Assert.Equal("sale_discount_too_high", ex.Code);
    }
}
