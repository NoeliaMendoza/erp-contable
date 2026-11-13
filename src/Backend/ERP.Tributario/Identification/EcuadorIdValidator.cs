namespace ERP.Tributario.Identification;

public enum TaxpayerKind
{
    Desconocido = 0,
    PersonaNatural = 1,
    SociedadPrivada = 2,
    SociedadPublica = 3,
}

/// <summary>Resultado de una validación <b>sintáctica</b>. No equivale a consultar el estado real en el SRI.</summary>
public sealed record IdValidationResult(
    bool IsValid,
    TaxpayerKind Kind,
    bool CheckDigitVerified,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    internal static IdValidationResult Fail(params string[] errors) =>
        new(false, TaxpayerKind.Desconocido, false, errors, []);
}

/// <summary>
/// Validación sintáctica de cédula y RUC ecuatorianos.
/// <para>
/// Cédula: 10 dígitos, código de provincia, tercer dígito &lt; 6 y dígito verificador módulo 10
/// (algoritmo de dominio público; ver docs/ecuador/matriz-normativa.md, estado: pendiente de
/// confirmación con fuente oficial del Registro Civil/SRI).
/// </para>
/// <para>
/// RUC de sociedades (tercer dígito 6 o 9): se valida estructura. El dígito verificador módulo 11 se
/// calcula solo como <b>advertencia</b>, porque existen reportes de RUC emitidos que no cumplen el
/// algoritmo; rechazar por esa causa debe decidirse tras verificar con el SRI (ver decisiones pendientes).
/// </para>
/// </summary>
public static class EcuadorIdValidator
{
    private const int ForeignResidentsProvince = 30;
    private const int MaxProvince = 24;

    public static IdValidationResult ValidateCedula(string? value)
    {
        if (!IsDigits(value, 10))
        {
            return IdValidationResult.Fail("La cédula debe tener exactamente 10 dígitos.");
        }

        var digits = ToDigits(value!);
        if (!IsValidProvince(digits))
        {
            return IdValidationResult.Fail("Código de provincia inválido en la cédula.");
        }

        if (digits[2] > 5)
        {
            return IdValidationResult.Fail("El tercer dígito de una cédula debe estar entre 0 y 5.");
        }

        return Modulo10(digits) == digits[9]
            ? new IdValidationResult(true, TaxpayerKind.PersonaNatural, true, [], [])
            : IdValidationResult.Fail("Dígito verificador de cédula inválido.");
    }

    public static IdValidationResult ValidateRuc(string? value)
    {
        if (!IsDigits(value, 13))
        {
            return IdValidationResult.Fail("El RUC debe tener exactamente 13 dígitos.");
        }

        var digits = ToDigits(value!);
        if (!IsValidProvince(digits))
        {
            return IdValidationResult.Fail("Código de provincia inválido en el RUC.");
        }

        switch (digits[2])
        {
            case <= 5:
                {
                    var cedula = ValidateCedula(value![..10]);
                    if (!cedula.IsValid)
                    {
                        return IdValidationResult.Fail(["RUC de persona natural con cédula base inválida.", .. cedula.Errors]);
                    }

                    return value[10..] == "000"
                        ? IdValidationResult.Fail("El número de establecimiento del RUC no puede ser 000.")
                        : new IdValidationResult(true, TaxpayerKind.PersonaNatural, true, [], []);
                }

            case 6:
                {
                    if (value![9..] == "0000")
                    {
                        return IdValidationResult.Fail("El sufijo de establecimiento del RUC público no puede ser 0000.");
                    }

                    var matches = Modulo11(digits, [3, 2, 7, 6, 5, 4, 3, 2]) == digits[8];
                    return Structural(TaxpayerKind.SociedadPublica, matches);
                }

            case 9:
                {
                    if (value![10..] == "000")
                    {
                        return IdValidationResult.Fail("El número de establecimiento del RUC no puede ser 000.");
                    }

                    var matches = Modulo11(digits, [4, 3, 2, 7, 6, 5, 4, 3, 2]) == digits[9];
                    return Structural(TaxpayerKind.SociedadPrivada, matches);
                }

            default:
                return IdValidationResult.Fail("Tercer dígito de RUC no reconocido (se espera 0-5, 6 o 9).");
        }
    }

    private static IdValidationResult Structural(TaxpayerKind kind, bool checkDigitMatches) =>
        new(
            IsValid: true,
            Kind: kind,
            CheckDigitVerified: false,
            Errors: [],
            Warnings: checkDigitMatches
                ? []
                : ["El dígito verificador módulo 11 no coincide; confirmar el RUC con el SRI antes de emitir comprobantes."]);

    private static bool IsDigits(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiDigit);

    private static int[] ToDigits(string value) => [.. value.Select(c => c - '0')];

    private static bool IsValidProvince(int[] digits)
    {
        var province = (digits[0] * 10) + digits[1];
        return province is >= 1 and <= MaxProvince or ForeignResidentsProvince;
    }

    private static int Modulo10(int[] digits)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var product = digits[i] * (i % 2 == 0 ? 2 : 1);
            sum += product > 9 ? product - 9 : product;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static int Modulo11(int[] digits, int[] coefficients)
    {
        var sum = 0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            sum += digits[i] * coefficients[i];
        }

        var remainder = sum % 11;
        return remainder == 0 ? 0 : 11 - remainder;
    }
}
