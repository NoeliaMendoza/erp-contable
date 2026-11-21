namespace ERP.Tributario.Identification;

public enum EcuadorIdentificationSubject
{
    Customer,
    Supplier,
}

/// <summary>Entry in the shared SRI identification-type catalog.</summary>
public sealed record EcuadorIdentificationType(
    string SriCode,
    string Name,
    int? ExactLength,
    int? MaximumLength,
    string? FixedNumber,
    bool AvailableForSuppliers);

/// <summary>Normalized identifier and its syntactic validation result.</summary>
public sealed record EcuadorIdentificationResult(
    bool IsValid,
    string SriCode,
    string NormalizedNumber,
    string? Error);

/// <summary>
/// Shared identification catalog for customers and suppliers. RUC and national identity card
/// checks delegate to <see cref="EcuadorIdValidator"/>; this catalog owns type codes and normalization.
/// </summary>
public static class EcuadorIdentificationCatalog
{
    public const string RucCode = "04";
    public const string CedulaCode = "05";
    public const string PassportCode = "06";
    public const string FinalConsumerCode = "07";
    public const string ForeignIdentificationCode = "08";
    public const string FinalConsumerNumber = "9999999999999";

    private static readonly EcuadorIdentificationType[] Entries =
    [
        new(RucCode, "RUC", 13, 13, null, true),
        new(CedulaCode, "Cédula", 10, 10, null, true),
        new(PassportCode, "Pasaporte", null, 20, null, true),
        new(FinalConsumerCode, "Consumidor final", 13, 13, FinalConsumerNumber, false),
        new(ForeignIdentificationCode, "Identificación del exterior", null, null, null, true),
    ];

    public static IReadOnlyList<EcuadorIdentificationType> All { get; } = Array.AsReadOnly(Entries);

    public static IReadOnlyList<EcuadorIdentificationType> ForCustomers => All;

    public static IReadOnlyList<EcuadorIdentificationType> ForSuppliers { get; } =
        Array.AsReadOnly(Entries.Where(entry => entry.AvailableForSuppliers).ToArray());

    public static bool TryGetType(string? sriCode, out EcuadorIdentificationType type)
    {
        type = Entries.FirstOrDefault(entry =>
            string.Equals(entry.SriCode, sriCode?.Trim(), StringComparison.Ordinal))!;
        return type is not null;
    }

    public static EcuadorIdentificationResult Validate(
        string? sriCode,
        string? value,
        EcuadorIdentificationSubject subject)
    {
        if (!TryGetType(sriCode, out var type))
        {
            return Invalid(sriCode, value, "Seleccione un tipo de identificación válido.");
        }

        var normalized = Normalize(value, type.SriCode);
        if (subject == EcuadorIdentificationSubject.Supplier && !type.AvailableForSuppliers)
        {
            return Invalid(type.SriCode, normalized, "El tipo consumidor final no está permitido para proveedores.");
        }

        if (normalized.Length == 0)
        {
            return Invalid(type.SriCode, normalized, "Ingrese el número de identificación.");
        }

        if (type.MaximumLength is { } maximum && normalized.Length > maximum)
        {
            return Invalid(type.SriCode, normalized, $"La identificación admite como máximo {maximum} caracteres.");
        }

        var error = type.SriCode switch
        {
            RucCode when !EcuadorIdValidator.ValidateRuc(normalized).IsValid =>
                string.Join(" ", EcuadorIdValidator.ValidateRuc(normalized).Errors),
            CedulaCode when !EcuadorIdValidator.ValidateCedula(normalized).IsValid =>
                string.Join(" ", EcuadorIdValidator.ValidateCedula(normalized).Errors),
            FinalConsumerCode when !string.Equals(normalized, FinalConsumerNumber, StringComparison.Ordinal) =>
                $"El número para consumidor final debe ser {FinalConsumerNumber}.",
            _ => null,
        };

        return error is null
            ? new EcuadorIdentificationResult(true, type.SriCode, normalized, null)
            : Invalid(type.SriCode, normalized, error);
    }

    private static string Normalize(string? value, string sriCode)
    {
        var compact = string.Concat((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && character != '-'));
        return sriCode == PassportCode ? compact.ToUpperInvariant() : compact;
    }

    private static EcuadorIdentificationResult Invalid(string? sriCode, string? number, string error) =>
        new(false, sriCode?.Trim() ?? string.Empty, number ?? string.Empty, error);
}
