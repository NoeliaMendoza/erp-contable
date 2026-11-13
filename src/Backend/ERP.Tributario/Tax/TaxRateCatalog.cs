using ERP.BuildingBlocks.Domain;

namespace ERP.Tributario.Tax;

public enum TaxKind
{
    Iva = 1,
    RetencionIva = 2,
    RetencionRenta = 3,
}

/// <summary>Estado de verificación de un valor tributario frente a la fuente oficial.</summary>
public enum VerificationStatus
{
    /// <summary>Valor sintético para pruebas. Nunca usar para emitir.</summary>
    Sintetico = 0,

    /// <summary>Valor conocido pero no contrastado con la fuente oficial vigente.</summary>
    PendienteVerificacion = 1,

    /// <summary>Contrastado con la fuente oficial (URL/versión/fecha en <see cref="TaxRateDefinition.SourceReference"/>).</summary>
    Verificado = 2,
}

/// <summary>
/// Tarifa tributaria con vigencia. Ninguna tarifa (p. ej. IVA) se codifica como constante:
/// siempre se resuelve por código, tipo de impuesto y fecha del hecho generador.
/// </summary>
public sealed record TaxRateDefinition(
    TaxKind Kind,
    string SriCode,
    string Description,
    decimal Percentage,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string SourceReference,
    VerificationStatus Status)
{
    public bool IsEffectiveOn(DateOnly date) =>
        date >= ValidFrom && (ValidTo is null || date <= ValidTo);
}

/// <summary>
/// Catálogo inmutable de tarifas versionadas. Valida que no existan vigencias superpuestas para el mismo
/// (impuesto, código) y resuelve la tarifa aplicable a una fecha.
/// </summary>
public sealed class TaxRateCatalog
{
    private readonly IReadOnlyList<TaxRateDefinition> _rates;

    public TaxRateCatalog(string version, IEnumerable<TaxRateDefinition> rates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Version = version;
        _rates = [.. rates];

        foreach (var rate in _rates)
        {
            if (rate.Percentage is < 0 or > 100)
            {
                throw new DomainException("tax_rate_invalid", $"Porcentaje fuera de rango en {rate.Kind}/{rate.SriCode}.");
            }

            if (rate.ValidTo is not null && rate.ValidTo < rate.ValidFrom)
            {
                throw new DomainException("tax_rate_invalid", $"Vigencia invertida en {rate.Kind}/{rate.SriCode}.");
            }
        }

        var overlaps = _rates
            .GroupBy(r => (r.Kind, r.SriCode))
            .Where(g => g.OrderBy(r => r.ValidFrom).Zip(g.OrderBy(r => r.ValidFrom).Skip(1))
                .Any(pair => pair.First.ValidTo is null || pair.First.ValidTo >= pair.Second.ValidFrom));

        if (overlaps.Any())
        {
            throw new DomainException("tax_rate_overlap", "El catálogo contiene vigencias superpuestas para un mismo impuesto y código.");
        }
    }

    public string Version { get; }

    public IReadOnlyList<TaxRateDefinition> Rates => _rates;

    /// <summary>Resuelve la tarifa vigente. Lanza si no existe: nunca se asume un valor por defecto.</summary>
    public TaxRateDefinition Resolve(TaxKind kind, string sriCode, DateOnly date) =>
        _rates.SingleOrDefault(r => r.Kind == kind && r.SriCode == sriCode && r.IsEffectiveOn(date))
        ?? throw new DomainException(
            "tax_rate_not_found",
            $"No existe tarifa vigente para {kind} código '{sriCode}' en {date:yyyy-MM-dd} (catálogo {Version}).");
}

/// <summary>Fuente del catálogo vigente (BD, archivo versionado, etc.). Implementación pendiente.</summary>
public interface ITaxRateCatalogProvider
{
    Task<TaxRateCatalog> GetCatalogAsync(CancellationToken cancellationToken);
}
