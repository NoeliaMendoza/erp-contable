using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using ERP.Modules.Inventory.Validators;

namespace ERP.UnitTests.Inventory;

public sealed class InventoryModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" und ", "UND")]
    [InlineData("Kg", "KG")]
    public void Codigo_de_unidad_se_normaliza(string input, string expected)
    {
        Assert.Equal(expected, Unit.Create(Guid.NewGuid(), Tenant, input, "Unidad", Now, null).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CODIGOLARGO")]
    public void Codigo_de_unidad_invalido_se_rechaza(string code)
    {
        Assert.Throws<DomainException>(() => Unit.Create(Guid.NewGuid(), Tenant, code, "Unidad", Now, null));
    }

    [Fact]
    public void Sku_se_normaliza_y_el_precio_conserva_decimales()
    {
        var product = Product.Create(Guid.NewGuid(), Tenant, " cuad-001 ", "Cuaderno", ProductKind.Product, UnitId, 5.1234m, Now, null);

        Assert.Equal("CUAD-001", product.Sku);
        Assert.Equal(5.1234m, product.Price);
        Assert.True(product.IsActive);
    }

    [Theory]
    [InlineData(-0.01, "product_price_negative")]
    [InlineData(1.00001, "product_price_scale")]
    public void Precio_invalido_se_rechaza_sin_redondear(double price, string code)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, "X", "Artículo", ProductKind.Product, UnitId, (decimal)price, Now, null));

        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Articulo_requiere_unidad_y_tipo_valido()
    {
        Assert.Equal("product_unit_required", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, "X", "Artículo", ProductKind.Service, Guid.Empty, 1m, Now, null)).Code);
        Assert.Equal("product_kind_invalid", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, "X", "Artículo", (ProductKind)9, UnitId, 1m, Now, null)).Code);
    }

    [Fact]
    public void Baja_logica_registra_quien_y_cuando()
    {
        var product = Product.Create(Guid.NewGuid(), Tenant, "X", "Artículo", ProductKind.Product, UnitId, 1m, Now, null);
        var user = Guid.NewGuid();

        product.SetActive(false, Now.AddHours(1), user);

        Assert.False(product.IsActive);
        Assert.Equal(user, product.UpdatedBy);
        Assert.Equal(Now.AddHours(1), product.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("Product", true)]
    [InlineData("service", true)]
    [InlineData("SERVICE", true)]
    [InlineData("1", false)]
    [InlineData("Mercaderia", false)]
    [InlineData("", false)]
    public void Tipo_acepta_solo_nombres_validos(string value, bool valid)
    {
        Assert.Equal(valid, new SaveProductRequestValidator()
            .Validate(new SaveProductRequest("SKU1", "Artículo", value, UnitId, 1m)).IsValid);
    }

    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(3, 500, 3, 100)]
    [InlineData(2, 10, 2, 10)]
    public void Paginacion_ajusta_valores_fuera_de_rango(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);

        Assert.Equal(expectedPage, request.SafePage);
        Assert.Equal(expectedSize, request.SafePageSize);
        Assert.Equal((expectedPage - 1) * expectedSize, request.Skip);
    }
}
