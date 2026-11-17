using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Inventory.Dtos;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// INV-01: catálogo de unidades y productos/servicios contra PostgreSQL real con RLS.
/// Ana administra Andina, Bruno administra Pacífico, Carla solo consulta en Pacífico.
/// Cada prueba usa códigos únicos para no depender del orden de ejecución.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;

    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    private static async Task<UnitResponse> CreateUnitAsync(HttpClient client, string? code = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/units", new SaveUnitRequest(code ?? NewCode("U"), "Unidad de prueba"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UnitResponse>(Ct))!;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, Guid unitId, string? sku = null, string kind = "Product", decimal price = 5m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(sku ?? NewCode("P"), "Artículo de prueba", kind, unitId, price), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(Ct))!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // ── Unidades ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unidad_se_crea_normalizada_y_se_puede_editar()
    {
        var code = NewCode("kg");
        var created = await CreateUnitAsync(_ana, "  " + code.ToLowerInvariant() + " ");

        Assert.Equal(code, created.Code);
        Assert.True(created.IsActive);

        var updated = await _ana.PutAsJsonAsync($"/api/v1/units/{created.Id}", new SaveUnitRequest(code, "Kilogramo"), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Kilogramo", (await updated.Content.ReadFromJsonAsync<UnitResponse>(Ct))!.Name);
    }

    [Fact]
    public async Task Codigo_de_unidad_duplicado_en_la_misma_empresa_responde_409()
    {
        var code = NewCode("D");
        await CreateUnitAsync(_ana, code);

        var duplicate = await _ana.PostAsJsonAsync("/api/v1/units", new SaveUnitRequest(code.ToLowerInvariant(), "Otra"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("unit_code_duplicated", await ProblemCodeAsync(duplicate));
    }

    [Fact]
    public async Task El_mismo_codigo_de_unidad_se_permite_en_otra_empresa()
    {
        var code = NewCode("S");
        await CreateUnitAsync(_ana, code);

        var other = await CreateUnitAsync(_bruno, code);

        Assert.Equal(code, other.Code);
    }

    [Fact]
    public async Task No_se_desactiva_una_unidad_usada_por_articulos_activos()
    {
        var unit = await CreateUnitAsync(_ana);
        var product = await CreateProductAsync(_ana, unit.Id);

        var blocked = await _ana.PatchAsJsonAsync($"/api/v1/units/{unit.Id}/status", new ChangeStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("unit_in_use", await ProblemCodeAsync(blocked));

        await _ana.PatchAsJsonAsync($"/api/v1/products/{product.Id}/status", new ChangeStatusRequest(false), Ct);
        var allowed = await _ana.PatchAsJsonAsync($"/api/v1/units/{unit.Id}/status", new ChangeStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.False((await allowed.Content.ReadFromJsonAsync<UnitResponse>(Ct))!.IsActive);
    }

    // ── Productos y servicios ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Producto_y_servicio_se_crean_con_su_unidad()
    {
        var unit = await CreateUnitAsync(_ana);
        var product = await CreateProductAsync(_ana, unit.Id, price: 5.25m);
        var service = await CreateProductAsync(_ana, unit.Id, kind: "service", price: 25m);

        Assert.Equal("Product", product.Kind);
        Assert.Equal(5.25m, product.Price);
        Assert.Equal(unit.Code, product.UnitCode);
        Assert.Equal("Service", service.Kind);

        var fetched = await _ana.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}", Ct);
        Assert.Equal(product.Sku, fetched!.Sku);
    }

    [Fact]
    public async Task Sku_duplicado_en_la_misma_empresa_responde_409_y_se_permite_en_otra()
    {
        var sku = NewCode("SKU");
        var anaUnit = await CreateUnitAsync(_ana);
        var brunoUnit = await CreateUnitAsync(_bruno);
        await CreateProductAsync(_ana, anaUnit.Id, sku);

        var duplicate = await _ana.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(sku.ToLowerInvariant(), "Copia", "Product", anaUnit.Id, 1m), Ct);
        var otherTenant = await _bruno.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(sku, "Igual en otra empresa", "Product", brunoUnit.Id, 1m), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("product_sku_duplicated", await ProblemCodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.Created, otherTenant.StatusCode);
    }

    [Theory]
    [InlineData("Product", -1, "Price")]
    [InlineData("Product", 1.12345, "Price")]
    [InlineData("Mercaderia", 1, "Kind")]
    [InlineData("1", 1, "Kind")]
    public async Task Datos_invalidos_responden_400_con_el_campo(string kind, double price, string field)
    {
        var unit = await CreateUnitAsync(_ana);

        var response = await _ana.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(NewCode("X"), "Inválido", kind, unit.Id, (decimal)price), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_unidad_de_otra_empresa_se_rechaza_aunque_su_id_exista()
    {
        var foreignUnit = await CreateUnitAsync(_bruno);

        var response = await _ana.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(NewCode("F"), "Con unidad ajena", "Product", foreignUnit.Id, 1m), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unit_not_available", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Una_unidad_inactiva_no_se_asigna_a_articulos_nuevos()
    {
        var unit = await CreateUnitAsync(_ana);
        await _ana.PatchAsJsonAsync($"/api/v1/units/{unit.Id}/status", new ChangeStatusRequest(false), Ct);

        var response = await _ana.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(NewCode("I"), "Con unidad inactiva", "Product", unit.Id, 1m), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unit_inactive", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Baja_logica_conserva_el_articulo_y_se_puede_filtrar()
    {
        var unit = await CreateUnitAsync(_ana);
        var product = await CreateProductAsync(_ana, unit.Id);

        var off = await _ana.PatchAsJsonAsync($"/api/v1/products/{product.Id}/status", new ChangeStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var detail = await _ana.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}", Ct);
        var actives = await _ana.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/v1/products?isActive=true&search={product.Sku}", Ct);
        Assert.False(detail!.IsActive);
        Assert.Empty(actives!.Items);
    }

    [Fact]
    public async Task Filtro_por_tipo_devuelve_solo_servicios()
    {
        var unit = await CreateUnitAsync(_ana);
        var prefix = NewCode("K");
        await CreateProductAsync(_ana, unit.Id, prefix + "-P", "Product");
        await CreateProductAsync(_ana, unit.Id, prefix + "-S", "Service");

        var services = await _ana.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/v1/products?kind=Service&search={prefix}", Ct);

        Assert.Equal([prefix + "-S"], services!.Items.Select(p => p.Sku));
    }

    // ── Aislamiento y permisos ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Otra_empresa_no_puede_leer_editar_ni_desactivar_por_id()
    {
        var unit = await CreateUnitAsync(_ana);
        var product = await CreateProductAsync(_ana, unit.Id);
        var brunoUnit = await CreateUnitAsync(_bruno);

        var read = await _bruno.GetAsync($"/api/v1/products/{product.Id}", Ct);
        var edit = await _bruno.PutAsJsonAsync($"/api/v1/products/{product.Id}", new SaveProductRequest("HACK", "Intruso", "Product", brunoUnit.Id, 0m), Ct);
        var disable = await _bruno.PatchAsJsonAsync($"/api/v1/products/{product.Id}/status", new ChangeStatusRequest(false), Ct);
        var unitRead = await _bruno.GetAsync($"/api/v1/units/{unit.Id}", Ct);
        var list = await _bruno.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/v1/products?search={product.Sku}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unitRead.StatusCode);
        Assert.Empty(list!.Items);

        var stillThere = await _ana.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}", Ct);
        Assert.True(stillThere!.IsActive);
    }

    [Fact]
    public async Task Rol_de_consulta_lee_pero_no_escribe()
    {
        var carla = _api.ClientFor(DevelopmentSeeder.CarlaEmail, Pacifico);
        var brunoUnit = await CreateUnitAsync(_bruno);

        var read = await carla.GetAsync("/api/v1/products", Ct);
        var createUnit = await carla.PostAsJsonAsync("/api/v1/units", new SaveUnitRequest(NewCode("C"), "No permitida"), Ct);
        var createProduct = await carla.PostAsJsonAsync("/api/v1/products", new SaveProductRequest(NewCode("C"), "No permitido", "Product", brunoUnit.Id, 1m), Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createUnit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createProduct.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401_y_sin_empresa_400()
    {
        var anonymous = await _api.ClientFor(null).GetAsync("/api/v1/products", Ct);
        var noTenant = await _api.ClientFor(DevelopmentSeeder.AnaEmail).GetAsync("/api/v1/units", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noTenant.StatusCode);
    }

    // ── Paginación ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paginacion_y_busqueda_son_estables()
    {
        var prefix = NewCode("PG");
        foreach (var suffix in new[] { "A", "B", "C" })
        {
            await CreateUnitAsync(_ana, prefix + suffix);
        }

        var first = await _ana.GetFromJsonAsync<PagedResult<UnitResponse>>($"/api/v1/units?search={prefix}&pageSize=2&page=1", Ct);
        var second = await _ana.GetFromJsonAsync<PagedResult<UnitResponse>>($"/api/v1/units?search={prefix}&pageSize=2&page=2", Ct);
        var clamped = await _ana.GetFromJsonAsync<PagedResult<UnitResponse>>($"/api/v1/units?pageSize=500&page=0", Ct);

        Assert.Equal(3, first!.TotalCount);
        Assert.Equal([prefix + "A", prefix + "B"], first.Items.Select(u => u.Code));
        Assert.Equal([prefix + "C"], second!.Items.Select(u => u.Code));
        Assert.Equal(PageRequest.MaxPageSize, clamped!.PageSize);
        Assert.Equal(1, clamped.Page);
    }

    [Fact]
    public async Task Busqueda_trata_los_comodines_como_texto()
    {
        await CreateUnitAsync(_ana);

        var wildcard = await _ana.GetFromJsonAsync<PagedResult<UnitResponse>>("/api/v1/units?search=%25", Ct);

        Assert.Equal(0, wildcard!.TotalCount);
    }
}
