using ERP.BuildingBlocks.Domain;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Models;
using ERP.Modules.Purchases.Validators;

namespace ERP.UnitTests.Purchases;

public sealed class SupplierTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Identificacion_se_recorta_y_normaliza_para_evitar_duplicados_por_mayusculas()
    {
        var supplier = Supplier.Create(Guid.NewGuid(), Tenant,
            new SupplierData("06", " ab-123 ", " Proveedor S.A. ", null, null, null), Now, null);

        Assert.Equal("06", supplier.IdentificationType);
        Assert.Equal("AB123", supplier.IdentificationNumber);
        Assert.Equal("Proveedor S.A.", supplier.Name);
    }

    [Fact]
    public void Baja_logica_conserva_identidad_y_registra_auditoria()
    {
        var user = Guid.NewGuid();
        var supplier = Supplier.Create(Guid.NewGuid(), Tenant,
            new SupplierData("04", "1799999990001", "Proveedor", null, null, null), Now, null);

        supplier.SetActive(false, Now.AddMinutes(2), user);

        Assert.False(supplier.IsActive);
        Assert.Equal(user, supplier.UpdatedBy);
        Assert.Equal(Now.AddMinutes(2), supplier.UpdatedAtUtc);
        Assert.Equal("1799999990001", supplier.IdentificationNumber);
    }

    [Theory]
    [InlineData("", "1799999990001", "Nombre", null, false)]
    [InlineData("04", "", "Nombre", null, false)]
    [InlineData("04", "1799999990001", "", null, false)]
    [InlineData("04", "1799999990001", "Nombre", "correo-no-valido", false)]
    [InlineData("04", "1799999990001", "Nombre", null, true)]
    [InlineData("05", " 0102-030400 ", "Nombre", "persona@example.com", true)]
    [InlineData("06", " ab-c 123 ", "Nombre", null, true)]
    [InlineData("07", "9999999999999", "Proveedor", null, false)]
    [InlineData("04", "1799999990000", "Nombre", null, false)]
    public void Validador_aplica_catalogo_y_correo_opcional(string type, string number, string name, string? email, bool valid)
    {
        var result = new SaveSupplierRequestValidator().Validate(
            new SaveSupplierRequest(type, number, name, email, null, null));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Dominio_rechaza_identificacion_vacia_o_mayor_a_longitud()
    {
        Assert.Throws<DomainException>(() => Supplier.Create(Guid.NewGuid(), Tenant,
            new SupplierData("08", " ", "Proveedor", null, null, null), Now, null));
        Assert.Throws<DomainException>(() => Supplier.Create(Guid.NewGuid(), Tenant,
            new SupplierData("06", new string('1', 21), "Proveedor", null, null, null), Now, null));
    }
}
