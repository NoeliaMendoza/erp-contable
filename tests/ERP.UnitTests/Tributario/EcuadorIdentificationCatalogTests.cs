using ERP.Tributario.Identification;

namespace ERP.UnitTests.Tributario;

public sealed class EcuadorIdentificationCatalogTests
{
    [Fact]
    public void Clientes_y_proveedores_comparten_los_mismos_tipos_excepto_consumidor_final()
    {
        Assert.Equal(["04", "05", "06", "07", "08"],
            EcuadorIdentificationCatalog.ForCustomers.Select(type => type.SriCode));
        Assert.Equal(["04", "05", "06", "08"],
            EcuadorIdentificationCatalog.ForSuppliers.Select(type => type.SriCode));
    }

    [Theory]
    [InlineData("04", " 179-999999-0001 ", "1799999990001")]
    [InlineData("05", " 0102-030400 ", "0102030400")]
    [InlineData("06", " ab-c 123 ", "ABC123")]
    [InlineData("08", " ab-c 123 ", "abc123")]
    public void Normaliza_espacios_y_guiones_y_solo_mayusculiza_pasaporte(
        string type, string input, string expected)
    {
        var result = EcuadorIdentificationCatalog.Validate(
            type, input, EcuadorIdentificationSubject.Supplier);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.NormalizedNumber);
    }

    [Fact]
    public void Ruc_y_cedula_delegan_la_validacion_sintactica_existente()
    {
        Assert.True(EcuadorIdentificationCatalog.Validate("04", "1799999990001", EcuadorIdentificationSubject.Supplier).IsValid);
        Assert.False(EcuadorIdentificationCatalog.Validate("04", "1799999990000", EcuadorIdentificationSubject.Supplier).IsValid);
        Assert.True(EcuadorIdentificationCatalog.Validate("05", "0102030400", EcuadorIdentificationSubject.Supplier).IsValid);
        Assert.False(EcuadorIdentificationCatalog.Validate("05", "0102030401", EcuadorIdentificationSubject.Supplier).IsValid);
    }

    [Fact]
    public void Consumidor_final_es_valido_solo_para_clientes()
    {
        Assert.True(EcuadorIdentificationCatalog.Validate(
            "07", EcuadorIdentificationCatalog.FinalConsumerNumber, EcuadorIdentificationSubject.Customer).IsValid);
        Assert.False(EcuadorIdentificationCatalog.Validate(
            "07", EcuadorIdentificationCatalog.FinalConsumerNumber, EcuadorIdentificationSubject.Supplier).IsValid);
    }

    [Fact]
    public void Pasaporte_no_puede_superar_20_caracteres_y_codigo_de_tipo_debe_existir()
    {
        Assert.False(EcuadorIdentificationCatalog.Validate(
            "06", new string('a', 21), EcuadorIdentificationSubject.Supplier).IsValid);
        Assert.False(EcuadorIdentificationCatalog.Validate(
            "99", "ABC", EcuadorIdentificationSubject.Supplier).IsValid);
    }
}
