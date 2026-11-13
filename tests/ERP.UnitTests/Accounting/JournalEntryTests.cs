using ERP.BuildingBlocks.Domain;
using ERP.Modules.Accounting.Models;

namespace ERP.UnitTests.Accounting;

public sealed class JournalEntryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Caja = Guid.NewGuid();
    private static readonly Guid Ventas = Guid.NewGuid();
    private static readonly Guid IvaPorPagar = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private static JournalEntry Draft() =>
        JournalEntry.CreateDraft(Guid.NewGuid(), Tenant, new DateOnly(2026, 11, 13), "Venta de contado (sintética)");

    [Fact]
    public void Asiento_cuadrado_se_contabiliza()
    {
        var entry = Draft();
        entry.AddLine(Caja, 115.00m, 0);
        entry.AddLine(Ventas, 0, 100.00m);
        entry.AddLine(IvaPorPagar, 0, 15.00m);

        entry.Post(Now, isPeriodOpen: true);

        Assert.Equal(JournalEntryStatus.Posted, entry.Status);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);
        Assert.Equal(Now, entry.PostedAtUtc);
    }

    [Fact]
    public void Asiento_descuadrado_no_se_contabiliza()
    {
        var entry = Draft();
        entry.AddLine(Caja, 100.00m, 0);
        entry.AddLine(Ventas, 0, 99.99m);

        var ex = Assert.Throws<DomainException>(() => entry.Post(Now, isPeriodOpen: true));

        Assert.Equal("journal_unbalanced", ex.Code);
        Assert.Equal(JournalEntryStatus.Draft, entry.Status);
    }

    [Fact]
    public void Periodo_cerrado_bloquea_la_contabilizacion()
    {
        var entry = Draft();
        entry.AddLine(Caja, 10m, 0);
        entry.AddLine(Ventas, 0, 10m);

        var ex = Assert.Throws<DomainException>(() => entry.Post(Now, isPeriodOpen: false));

        Assert.Equal("period_closed", ex.Code);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(1.001, 0)]
    public void Lineas_invalidas_se_rechazan(decimal debit, decimal credit)
    {
        Assert.Throws<DomainException>(() => Draft().AddLine(Caja, debit, credit));
    }

    [Fact]
    public void Requiere_al_menos_dos_lineas()
    {
        var entry = Draft();
        entry.AddLine(Caja, 10m, 0);

        Assert.Equal("journal_min_lines", Assert.Throws<DomainException>(() => entry.Post(Now, true)).Code);
    }

    [Fact]
    public void Asiento_contabilizado_es_inmutable()
    {
        var entry = Draft();
        entry.AddLine(Caja, 10m, 0);
        entry.AddLine(Ventas, 0, 10m);
        entry.Post(Now, true);

        Assert.Equal("journal_immutable", Assert.Throws<DomainException>(() => entry.AddLine(Caja, 1m, 0)).Code);
    }

    [Fact]
    public void Reverso_invierte_lineas_y_marca_el_original()
    {
        var entry = Draft();
        entry.AddLine(Caja, 115m, 0);
        entry.AddLine(Ventas, 0, 100m);
        entry.AddLine(IvaPorPagar, 0, 15m);
        entry.Post(Now, true);

        var reversal = entry.CreateReversal(Guid.NewGuid(), new DateOnly(2026, 11, 14), "error de digitación");
        reversal.Post(Now, true);

        Assert.Equal(JournalEntryStatus.Reversed, entry.Status);
        Assert.Equal(reversal.Id, entry.ReversedById);
        Assert.Equal(entry.Id, reversal.ReversalOfId);
        Assert.Equal(entry.TotalDebit, reversal.TotalCredit);
        Assert.Equal(115m, reversal.Lines.Single(l => l.AccountId == Caja).Credit);
        Assert.Throws<DomainException>(() => entry.CreateReversal(Guid.NewGuid(), new DateOnly(2026, 11, 15), "doble"));
    }
}
