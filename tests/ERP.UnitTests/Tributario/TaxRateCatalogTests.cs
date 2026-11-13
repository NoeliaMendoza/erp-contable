using ERP.BuildingBlocks.Domain;
using ERP.Tributario.Tax;

namespace ERP.UnitTests.Tributario;

/// <summary>
/// Catálogo con valores SINTÉTICOS (códigos "SYN-*"): no representan tarifas oficiales del SRI.
/// Prueban la mecánica de vigencias, no la normativa.
/// </summary>
public sealed class TaxRateCatalogTests
{
    private static TaxRateCatalog SyntheticCatalog() => new("sintetico-v1",
    [
        new(TaxKind.Iva, "SYN-A", "Tarifa sintética A (periodo 1)", 10m, new DateOnly(2020, 1, 1), new DateOnly(2023, 12, 31), "prueba", VerificationStatus.Sintetico),
        new(TaxKind.Iva, "SYN-A", "Tarifa sintética A (periodo 2)", 11m, new DateOnly(2024, 1, 1), null, "prueba", VerificationStatus.Sintetico),
        new(TaxKind.Iva, "SYN-0", "Tarifa sintética cero", 0m, new DateOnly(2020, 1, 1), null, "prueba", VerificationStatus.Sintetico),
    ]);

    [Theory]
    [InlineData(2022, 6, 30, 10)]
    [InlineData(2023, 12, 31, 10)]
    [InlineData(2024, 1, 1, 11)]
    [InlineData(2030, 1, 1, 11)]
    public void Resuelve_la_tarifa_vigente_por_fecha_del_hecho_generador(int y, int m, int d, int expected)
    {
        var rate = SyntheticCatalog().Resolve(TaxKind.Iva, "SYN-A", new DateOnly(y, m, d));

        Assert.Equal(expected, rate.Percentage);
    }

    [Fact]
    public void Sin_tarifa_vigente_lanza_en_lugar_de_asumir_un_valor()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SyntheticCatalog().Resolve(TaxKind.Iva, "SYN-A", new DateOnly(2019, 12, 31)));

        Assert.Equal("tax_rate_not_found", ex.Code);
    }

    [Fact]
    public void Rechaza_vigencias_superpuestas()
    {
        var ex = Assert.Throws<DomainException>(() => new TaxRateCatalog("malo",
        [
            new(TaxKind.Iva, "SYN-X", "x", 1m, new DateOnly(2020, 1, 1), new DateOnly(2024, 6, 30), "prueba", VerificationStatus.Sintetico),
            new(TaxKind.Iva, "SYN-X", "y", 2m, new DateOnly(2024, 1, 1), null, "prueba", VerificationStatus.Sintetico),
        ]));

        Assert.Equal("tax_rate_overlap", ex.Code);
    }

    [Fact]
    public void Rechaza_porcentajes_fuera_de_rango()
    {
        Assert.Throws<DomainException>(() => new TaxRateCatalog("malo",
        [
            new(TaxKind.RetencionRenta, "SYN-R", "x", 101m, new DateOnly(2020, 1, 1), null, "prueba", VerificationStatus.Sintetico),
        ]));
    }
}
