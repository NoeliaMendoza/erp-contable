using ERP.Tributario.Tax;

namespace ERP.Modules.Sales.Services;

/// <summary>
/// Catálogo sintético para desarrollo hasta verificar la fuente oficial vigente.
/// Impide asumir tarifas distintas por artículo o por constantes en Ventas.
/// </summary>
public sealed class SalesTaxRateCatalogProvider : ITaxRateCatalogProvider
{
    private readonly TaxRateCatalog _catalog = new("ventas-sintetico-v1",
    [
        new(TaxKind.Iva, "IVA-GENERAL", "Tarifa sintética de desarrollo", 15m,
            new DateOnly(2026, 1, 1), null, "Pendiente verificación oficial", VerificationStatus.Sintetico),
        new(TaxKind.Iva, "IVA-0", "Tarifa cero sintética", 0m,
            new DateOnly(2026, 1, 1), null, "Pendiente verificación oficial", VerificationStatus.Sintetico),
    ]);

    public Task<TaxRateCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_catalog);
}
