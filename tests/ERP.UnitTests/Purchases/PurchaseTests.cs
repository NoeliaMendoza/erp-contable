using ERP.BuildingBlocks.Domain;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Purchases.Models;

namespace ERP.UnitTests.Purchases;

public sealed class PurchaseTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Diez_cuadernos_a_tres_dolares_totalizan_treinta_y_generan_clave_estable()
    {
        var id = Guid.NewGuid();
        var purchase = Purchase.Create(id, Tenant, "C-001", Guid.NewGuid(), "Proveedor", new DateOnly(2026, 11, 24),
            Guid.NewGuid(), [new PurchaseLineData(Product(), 10m, 3m)], Now, null);

        Assert.Equal(30m, purchase.TotalCost);
        Assert.Equal($"purchases:receipt:{id:D}", purchase.ReceiptIdempotencyKey);
        Assert.Equal(30m, Assert.Single(purchase.Lines).LineCost);
        Assert.Equal(PurchaseStatus.Draft, purchase.Status);
    }

    [Fact]
    public void Compra_confirmada_es_idempotente_si_se_repite_con_las_mismas_referencias()
    {
        var purchase = Create();
        var movementId = Guid.NewGuid();
        var journalId = Guid.NewGuid();

        purchase.Confirm(movementId, journalId);
        purchase.Confirm(movementId, journalId);

        Assert.Equal(PurchaseStatus.Confirmed, purchase.Status);
        Assert.Equal(movementId, purchase.StockMovementId);
        Assert.Equal(journalId, purchase.JournalEntryId);
    }

    [Fact]
    public void Compra_no_acepta_una_segunda_confirmacion_con_otros_movimientos()
    {
        var purchase = Create();
        purchase.Confirm(Guid.NewGuid(), Guid.NewGuid());

        var error = Assert.Throws<ConflictException>(() => purchase.Confirm(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("purchase_confirmation_conflict", error.Code);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1.12345, 3)]
    [InlineData(1, -1)]
    [InlineData(1, 1.12345)]
    public void Linea_rechaza_cantidad_o_costo_fuera_de_regla(decimal quantity, decimal unitCost)
    {
        Assert.Throws<DomainException>(() => Create([new PurchaseLineData(Product(), quantity, unitCost)]));
    }

    [Fact]
    public void Compra_rechaza_articulos_repetidos()
    {
        var product = Product();
        var error = Assert.Throws<DomainException>(() => Create([
            new PurchaseLineData(product, 1m, 3m),
            new PurchaseLineData(product, 2m, 3m),
        ]));

        Assert.Equal("purchase_duplicate_product", error.Code);
    }

    private static Purchase Create(IReadOnlyCollection<PurchaseLineData>? lines = null) =>
        Purchase.Create(Guid.NewGuid(), Tenant, "C-TEST", Guid.NewGuid(), "Proveedor",
            new DateOnly(2026, 11, 24), Guid.NewGuid(), lines ?? [new PurchaseLineData(Product(), 1m, 3m)], Now, null);

    private static ProductSummary Product() => new(Guid.NewGuid(), "CUAD-001", "Cuaderno", false, "UND", true,
        VatTreatment.ZeroRated, true, null, 0m, 3m);
}
