using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Security;
using ERP.Modules.Platform.Models;
using ERP.Tributario.Identification;

namespace ERP.UnitTests.Platform;

public sealed class PlatformDomainTests
{
    [Fact]
    public void Contribuyente_con_RUC_sintetico_valido_se_crea_con_su_tipo()
    {
        var taxpayer = Taxpayer.Create(Guid.NewGuid(), Guid.NewGuid(), "0102030400001", "PERSONA DEMO", null, false, null, false, null, new DateOnly(2026, 1, 1));

        Assert.Equal(TaxpayerKind.PersonaNatural, taxpayer.Kind);
    }

    [Fact]
    public void Contribuyente_con_RUC_invalido_se_rechaza()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Taxpayer.Create(Guid.NewGuid(), Guid.NewGuid(), "123", "X", null, false, null, false, null, new DateOnly(2026, 1, 1)));

        Assert.Equal("ruc_invalid", ex.Code);
    }

    [Theory]
    [InlineData("000")]
    [InlineData("01")]
    [InlineData("A01")]
    public void Codigos_SRI_de_establecimiento_y_punto_de_emision_validan_formato(string code)
    {
        Assert.Throws<DomainException>(() => Establishment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, code, "x", null));
        Assert.Throws<DomainException>(() => EmissionPoint.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, "x"));
    }

    [Fact]
    public void Email_de_usuario_se_normaliza()
    {
        var user = AppUser.Create(Guid.NewGuid(), "  Ana.Admin@Demo.LOCAL ", "Ana", DateTimeOffset.UtcNow);

        Assert.Equal("ana.admin@demo.local", user.Email);
    }

    [Theory]
    [InlineData("perm:platform.branches.read", true, "platform.branches.read")]
    [InlineData("perm:", false, "")]
    [InlineData("otra", false, "")]
    public void Politicas_de_permiso_se_interpretan(string policy, bool ok, string code)
    {
        Assert.Equal(ok, PermissionPolicy.TryParse(policy, out var parsed));
        Assert.Equal(code, parsed);
    }
}
