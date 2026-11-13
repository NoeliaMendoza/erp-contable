using ERP.Tributario.Identification;

namespace ERP.UnitTests.Tributario;

/// <summary>Todos los números son sintéticos y calculados para las pruebas.</summary>
public sealed class EcuadorIdValidatorTests
{
    [Theory]
    [InlineData("0102030400")]
    [InlineData("1700000019")]
    public void Cedula_valida(string cedula)
    {
        var result = EcuadorIdValidator.ValidateCedula(cedula);

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.Equal(TaxpayerKind.PersonaNatural, result.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("010203040")]
    [InlineData("01020304000")]
    [InlineData("01020304AB")]
    [InlineData("0102030401")] // dígito verificador incorrecto
    [InlineData("2502030400")] // provincia inexistente
    [InlineData("0162030400")] // tercer dígito > 5
    public void Cedula_invalida(string? cedula)
    {
        Assert.False(EcuadorIdValidator.ValidateCedula(cedula).IsValid);
    }

    [Fact]
    public void Ruc_persona_natural_valido()
    {
        var result = EcuadorIdValidator.ValidateRuc("0102030400001");

        Assert.True(result.IsValid);
        Assert.Equal(TaxpayerKind.PersonaNatural, result.Kind);
        Assert.True(result.CheckDigitVerified);
    }

    [Fact]
    public void Ruc_persona_natural_con_establecimiento_000_es_invalido()
    {
        Assert.False(EcuadorIdValidator.ValidateRuc("0102030400000").IsValid);
    }

    [Fact]
    public void Ruc_sociedad_privada_se_valida_estructuralmente_sin_afirmar_digito()
    {
        var result = EcuadorIdValidator.ValidateRuc("1799999990001");

        Assert.True(result.IsValid);
        Assert.Equal(TaxpayerKind.SociedadPrivada, result.Kind);
        Assert.False(result.CheckDigitVerified);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Ruc_sociedad_privada_con_digito_no_coincidente_genera_advertencia_no_rechazo()
    {
        var result = EcuadorIdValidator.ValidateRuc("1799999991001");

        Assert.True(result.IsValid);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("179999999000")]   // 12 dígitos
    [InlineData("1779999990001")]  // tercer dígito 7 no reconocido
    [InlineData("1799999990000")]  // establecimiento 000
    [InlineData("9999999990001")]  // provincia inválida
    public void Ruc_invalido(string ruc)
    {
        Assert.False(EcuadorIdValidator.ValidateRuc(ruc).IsValid);
    }
}
