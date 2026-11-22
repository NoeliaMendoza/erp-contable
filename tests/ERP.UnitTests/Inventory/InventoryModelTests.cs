using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.Modules.Inventory.Contracts;
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
        var product = Product.Create(Guid.NewGuid(), Tenant, new ProductData(" cuad-001 ", "Cuaderno", ProductKind.Product, UnitId, 5.1234m, VatTreatment.Standard), Now, null);

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
            Product.Create(Guid.NewGuid(), Tenant, new ProductData("X", "Artículo", ProductKind.Product, UnitId, (decimal)price, VatTreatment.Standard), Now, null));

        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Articulo_requiere_unidad_y_tipo_valido()
    {
        Assert.Equal("product_unit_required", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, new ProductData("X", "Artículo", ProductKind.Service, Guid.Empty, 1m, VatTreatment.Standard), Now, null)).Code);
        Assert.Equal("product_kind_invalid", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, new ProductData("X", "Artículo", (ProductKind)9, UnitId, 1m, VatTreatment.Standard), Now, null)).Code);
    }

    [Fact]
    public void Baja_logica_registra_quien_y_cuando()
    {
        var product = Product.Create(Guid.NewGuid(), Tenant, new ProductData("X", "Artículo", ProductKind.Product, UnitId, 1m, VatTreatment.Standard), Now, null);
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
            .Validate(new SaveProductRequest("SKU1", "Artículo", value, UnitId, 1m, "Standard")).IsValid);
    }

    // ── Ficha del artículo (IVA, categoría, código de barras, costo, control de inventario) ──

    private static ProductData Data(
        ProductKind kind = ProductKind.Product, bool? tracks = null, decimal? minStock = null, decimal? cost = null,
        string? barcode = null, string? description = null, VatTreatment vat = VatTreatment.Standard) =>
        new("X", "Artículo", kind, UnitId, 1m, vat, null, barcode, description, cost, tracks, minStock);

    [Fact]
    public void Por_defecto_el_producto_controla_inventario_y_el_servicio_no()
    {
        Assert.True(Product.Create(Guid.NewGuid(), Tenant, Data(ProductKind.Product), Now, null).TracksInventory);
        Assert.False(Product.Create(Guid.NewGuid(), Tenant, Data(ProductKind.Service), Now, null).TracksInventory);
    }

    [Fact]
    public void Un_producto_puede_no_controlar_inventario()
    {
        Assert.False(Product.Create(Guid.NewGuid(), Tenant, Data(tracks: false), Now, null).TracksInventory);
    }

    [Fact]
    public void Un_servicio_no_puede_controlar_inventario()
    {
        var ex = Assert.Throws<DomainException>(() => Product.Create(Guid.NewGuid(), Tenant, Data(ProductKind.Service, tracks: true), Now, null));

        Assert.Equal("product_service_tracks_inventory", ex.Code);
    }

    [Fact]
    public void Stock_minimo_solo_si_controla_inventario()
    {
        Assert.Equal(10m, Product.Create(Guid.NewGuid(), Tenant, Data(minStock: 10m), Now, null).MinStock);
        Assert.Equal("product_min_stock_without_tracking", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, Data(tracks: false, minStock: 5m), Now, null)).Code);
        Assert.Equal("product_min_stock_negative", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, Data(minStock: -1m), Now, null)).Code);
    }

    [Theory]
    [InlineData(-0.01, "product_cost_negative")]
    [InlineData(1.00001, "product_cost_scale")]
    public void Costo_invalido_se_rechaza_sin_redondear(double cost, string code)
    {
        Assert.Equal(code, Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, Data(cost: (decimal)cost), Now, null)).Code);
    }

    [Fact]
    public void Iva_invalido_se_rechaza()
    {
        Assert.Equal("product_vat_invalid", Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, Data(vat: (VatTreatment)0), Now, null)).Code);
    }

    [Theory]
    [InlineData(" 786abc ", "786ABC")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Codigo_de_barras_se_normaliza_y_vacio_es_null(string? input, string? expected)
    {
        Assert.Equal(expected, Product.Create(Guid.NewGuid(), Tenant, Data(barcode: input), Now, null).Barcode);
    }

    [Fact]
    public void Descripcion_se_recorta_y_respeta_el_limite_de_la_factura()
    {
        Assert.Equal("Tapa dura", Product.Create(Guid.NewGuid(), Tenant, Data(description: "  Tapa dura "), Now, null).Description);
        Assert.Null(Product.Create(Guid.NewGuid(), Tenant, Data(description: "  "), Now, null).Description);
        Assert.Throws<DomainException>(() =>
            Product.Create(Guid.NewGuid(), Tenant, Data(description: new string('x', Product.DescriptionMaxLength + 1)), Now, null));
    }

    [Theory]
    [InlineData("  Papelería   escolar ", "Papelería escolar")]
    [InlineData("Bebidas", "Bebidas")]
    public void Nombre_de_categoria_se_normaliza(string input, string expected)
    {
        Assert.Equal(expected, Category.Create(Guid.NewGuid(), Tenant, input, Now, null).Name);
    }

    [Theory]
    [InlineData("Standard", true)]
    [InlineData("zerorated", true)]
    [InlineData("NotSubject", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("15", false)]
    [InlineData("Gravado", false)]
    public void Iva_acepta_solo_tratamientos_validos_y_es_obligatorio(string? value, bool valid)
    {
        var result = new SaveProductRequestValidator().Validate(new SaveProductRequest("SKU1", "Artículo", "Product", UnitId, 1m, value));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Validador_rechaza_servicio_con_stock_y_stock_minimo_sin_control()
    {
        var validator = new SaveProductRequestValidator();

        var service = validator.Validate(new SaveProductRequest("S1", "Servicio", "Service", UnitId, 1m, "Standard", TracksInventory: true));
        var serviceMin = validator.Validate(new SaveProductRequest("S2", "Servicio", "Service", UnitId, 1m, "Standard", MinStock: 1m));
        var productNoTracking = validator.Validate(new SaveProductRequest("P1", "Producto", "Product", UnitId, 1m, "Standard", TracksInventory: false, MinStock: 1m));
        var ok = validator.Validate(new SaveProductRequest("P2", "Producto", "Product", UnitId, 1m, "Standard", Barcode: "7861234567890", Cost: 0.5m, MinStock: 3m));

        Assert.Contains(service.Errors, e => e.PropertyName == "TracksInventory");
        Assert.Contains(serviceMin.Errors, e => e.PropertyName == "MinStock");
        Assert.Contains(productNoTracking.Errors, e => e.PropertyName == "MinStock");
        Assert.True(ok.IsValid);
    }

    [Theory]
    [InlineData("con espacio")]
    [InlineData("12345678901234567890123456")]
    public void Validador_rechaza_codigo_de_barras_invalido(string barcode)
    {
        var result = new SaveProductRequestValidator().Validate(
            new SaveProductRequest("P1", "Producto", "Product", UnitId, 1m, "Standard", Barcode: barcode));

        Assert.Contains(result.Errors, e => e.PropertyName == "Barcode");
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

    [Fact]
    public void Stock_salida_no_deja_cantidad_negativa()
    {
        var stock = StockBalance.Create(Tenant, Guid.NewGuid(), Guid.NewGuid());
        stock.Adjust(10m);

        stock.Adjust(-4m);

        Assert.Equal(6m, stock.QuantityOnHand);
        Assert.Equal("stock_insufficient", Assert.Throws<DomainException>(() => stock.Adjust(-7m)).Code);
        Assert.Equal(6m, stock.QuantityOnHand);
    }

    [Fact]
    public void Stock_respeta_cantidad_reservada()
    {
        var stock = StockBalance.Create(Tenant, Guid.NewGuid(), Guid.NewGuid());
        stock.Adjust(10m);
        stock.Reserve(4m);

        Assert.Equal("stock_insufficient", Assert.Throws<DomainException>(() => stock.Adjust(-7m)).Code);
        stock.Adjust(-6m);
        Assert.Equal(4m, stock.QuantityOnHand);
    }

    [Fact]
    public void Movimiento_de_servicio_no_modifica_stock()
    {
        var product = Product.Create(Guid.NewGuid(), Tenant,
            Data(ProductKind.Service), Now, null);

        Assert.False(product.TracksInventory);
        Assert.False(product.Kind == ProductKind.Product);
    }
}
