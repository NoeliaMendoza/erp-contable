using ERP.BuildingBlocks.Domain;
using ERP.Modules.Sales.Models;

namespace ERP.UnitTests.Sales;

public sealed class CustomerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cliente_se_crea_con_documento_normalizado()
    {
        var customer = Customer.Create(Guid.NewGuid(), Tenant,
            new CustomerData(IdentificationType.Cedula, " 1710034065 ", "  Cliente Demo  ",
                "  cliente@demo.local ", " 0991234567 ", " Quito "),
            Now, null);

        Assert.Equal(Tenant, customer.TenantId);
        Assert.Equal("1710034065", customer.IdentificationNumber);
        Assert.Equal("Cliente Demo", customer.Name);
        Assert.Equal("cliente@demo.local", customer.Email);
        Assert.True(customer.IsActive);
    }

    [Theory]
    [InlineData(IdentificationType.Ruc, "1799999990001", true)]
    [InlineData(IdentificationType.Ruc, "17999", false)]
    [InlineData(IdentificationType.Cedula, "1710034065", true)]
    [InlineData(IdentificationType.Cedula, "171003406A", false)]
    [InlineData(IdentificationType.Pasaporte, " X-PASS-1 ", true)]
    [InlineData(IdentificationType.ConsumidorFinal, "9999999999", true)]
    public void Numero_se_valida_por_tipo(IdentificationType type, string number, bool valid)
    {
        if (valid)
        {
            var normalized = Customer.NormalizeIdentificationNumber(type, number);
            Assert.False(string.IsNullOrWhiteSpace(normalized));
        }
        else
        {
            Assert.Throws<DomainException>(() =>
                Customer.NormalizeIdentificationNumber(type, number));
        }
    }

    [Fact]
    public void Baja_logica_conserva_auditoria()
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var customer = Customer.Create(Guid.NewGuid(), Tenant,
            new CustomerData(IdentificationType.Pasaporte, "PA123", "Pasajero", null, null, null),
            Now, creator);

        customer.SetActive(false, Now.AddHours(1), editor);

        Assert.False(customer.IsActive);
        Assert.Equal(creator, customer.CreatedBy);
        Assert.Equal(editor, customer.UpdatedBy);
        Assert.Equal(Now.AddHours(1), customer.UpdatedAtUtc);
    }
}
