using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.BuildingBlocks.Domain;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// INV-03: bodegas, entradas, salidas, existencias y kardex contra PostgreSQL real con RLS (ACUERDO-01).
/// Los movimientos se registran con el contrato <see cref="IInventoryMovementService"/>, como lo harán Compras y Ventas;
/// las existencias y el kardex se leen por HTTP. Cada prueba crea sus propios artículos.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryStockApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;
    private static readonly DateOnly Today = new(2026, 11, 20);

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

    // ── Ayudas ───────────────────────────────────────────────────────────────────────────────

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string kind = "Product", decimal? minStock = null)
    {
        var unit = await ReadAsync<UnitResponse>(
            await client.PostAsJsonAsync("/api/v1/units", new SaveUnitRequest(NewCode("U"), "Unidad"), Ct), HttpStatusCode.Created);
        var request = new SaveProductRequest(NewCode("P"), "Artículo " + kind, kind, unit.Id, 5m, "ZeroRated", MinStock: minStock);
        return await ReadAsync<ProductResponse>(await client.PostAsJsonAsync("/api/v1/products", request, Ct), HttpStatusCode.Created);
    }

    private static async Task<WarehouseResponse> DefaultWarehouseAsync(HttpClient client) =>
        (await ReadAsync<List<WarehouseResponse>>(await client.GetAsync("/api/v1/warehouses", Ct))).Single(w => w.IsDefault);

    private static InventoryMovementRequest Entrada(Guid warehouseId, params (Guid ProductId, decimal Quantity, decimal Cost)[] lines)
    {
        var id = Guid.CreateVersion7();
        return new InventoryMovementRequest($"purchases:receipt:{id:D}", "purchases", "receipt", id, warehouseId,
            InventoryMovementKind.Entrada, Today, lines.Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, l.Cost)).ToList());
    }

    private static InventoryMovementRequest Salida(Guid warehouseId, params (Guid ProductId, decimal Quantity)[] lines)
    {
        var id = Guid.CreateVersion7();
        return new InventoryMovementRequest($"sales:invoice:{id:D}", "sales", "invoice", id, warehouseId,
            InventoryMovementKind.Salida, Today, lines.Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, null)).ToList());
    }

    /// <summary>Llama al contrato como lo hace otro módulo: en su propio alcance, con la empresa ya resuelta.</summary>
    private async Task<InventoryMovementResult> RegisterAsync(Guid tenantId, InventoryMovementRequest request)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(tenantId, []);
        return await scope.ServiceProvider.GetRequiredService<IInventoryMovementService>().RegisterAsync(request, Ct);
    }

    private async Task<string> RejectedCodeAsync(Guid tenantId, InventoryMovementRequest request) =>
        (await Assert.ThrowsAnyAsync<DomainException>(() => RegisterAsync(tenantId, request))).Code;

    private async Task<StockItemResponse> StockOfAsync(HttpClient client, ProductResponse product, Guid? warehouseId = null)
    {
        var query = $"/api/v1/stock?search={product.Sku}" + (warehouseId is null ? string.Empty : $"&warehouseId={warehouseId}");
        var report = await ReadAsync<StockReportResponse>(await client.GetAsync(query, Ct));
        return report.Items.Items.Single(i => i.ProductId == product.Id);
    }

    // ── Ejemplo de la tarea y costo promedio ─────────────────────────────────────────────────

    [Fact]
    public async Task Entrada_10_a_3_y_salida_4_dejan_6_con_costo_de_salida_12()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);

        var entrada = await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 10m, 3m)));
        var salida = await RegisterAsync(Andina, Salida(warehouse.Id, (product.Id, 4m)));

        Assert.Equal(30m, entrada.TotalCost);
        Assert.Equal(12m, salida.TotalCost);
        Assert.False(salida.AlreadyProcessed);

        var stock = await StockOfAsync(_ana, product);
        Assert.Equal(6m, stock.QuantityOnHand);
        Assert.Equal(3m, stock.AverageCost);
        Assert.Equal(18m, stock.TotalValue);

        var kardex = await ReadAsync<KardexResponse>(await _ana.GetAsync($"/api/v1/products/{product.Id}/kardex", Ct));
        Assert.Equal(warehouse.Id, kardex.Warehouse.Id);
        Assert.Collection(kardex.Entries.Items,
            e => Assert.Equal(("Entrada", 10m, 0m, 30m, 10m), (e.Kind, e.QuantityIn, e.QuantityOut, e.LineCost, e.QuantityAfter!.Value)),
            e => Assert.Equal(("Salida", 0m, 4m, 12m, 6m), (e.Kind, e.QuantityIn, e.QuantityOut, e.LineCost, e.QuantityAfter!.Value)));
    }

    [Fact]
    public async Task Segunda_entrada_recalcula_el_promedio_movil()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 10m, 3m)));
        await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 10m, 4m)));

        var salida = await RegisterAsync(Andina, Salida(warehouse.Id, (product.Id, 5m)));

        Assert.Equal(17.50m, salida.TotalCost);
        Assert.Equal(3.5m, (await StockOfAsync(_ana, product)).AverageCost);
    }

    [Fact]
    public async Task Articulo_sin_movimientos_aparece_con_existencia_cero_y_bajo_minimo()
    {
        var product = await CreateProductAsync(_ana, minStock: 2m);

        var stock = await StockOfAsync(_ana, product);

        Assert.Equal(0m, stock.QuantityOnHand);
        Assert.True(stock.BelowMinimum);
    }

    // ── Sin stock negativo y concurrencia ────────────────────────────────────────────────────

    [Fact]
    public async Task Salida_sin_existencia_suficiente_se_rechaza_completa()
    {
        var a = await CreateProductAsync(_ana);
        var b = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        await RegisterAsync(Andina, Entrada(warehouse.Id, (a.Id, 5m, 2m), (b.Id, 1m, 2m)));

        var code = await RejectedCodeAsync(Andina, Salida(warehouse.Id, (a.Id, 2m), (b.Id, 3m)));

        Assert.Equal("inventory_insufficient_stock", code);
        Assert.Equal(5m, (await StockOfAsync(_ana, a)).QuantityOnHand);
        Assert.Equal(1m, (await StockOfAsync(_ana, b)).QuantityOnHand);
    }

    [Fact]
    public async Task Dos_ventas_simultaneas_no_consumen_la_misma_ultima_unidad()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 1m, 3m)));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await RegisterAsync(Andina, Salida(warehouse.Id, (product.Id, 1m)));
                return "ok";
            }
            catch (DomainException ex)
            {
                return ex.Code;
            }
        }));

        Assert.Single(attempts, a => a == "ok");
        Assert.All(attempts.Where(a => a != "ok"), a => Assert.Equal("inventory_insufficient_stock", a));
        Assert.Equal(0m, (await StockOfAsync(_ana, product)).QuantityOnHand);
    }

    // ── Idempotencia ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reintento_devuelve_el_movimiento_y_el_costo_originales()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 10m, 3m)));
        var salida = Salida(warehouse.Id, (product.Id, 4m));
        var first = await RegisterAsync(Andina, salida);

        await RegisterAsync(Andina, Entrada(warehouse.Id, (product.Id, 10m, 9m)));
        var retry = await RegisterAsync(Andina, salida);

        Assert.True(retry.AlreadyProcessed);
        Assert.Equal(first.MovementId, retry.MovementId);
        Assert.Equal(12m, retry.TotalCost);
        Assert.Equal(16m, (await StockOfAsync(_ana, product)).QuantityOnHand);
    }

    [Fact]
    public async Task Misma_clave_con_otros_datos_responde_conflicto()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        var entrada = Entrada(warehouse.Id, (product.Id, 10m, 3m));
        await RegisterAsync(Andina, entrada);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            RegisterAsync(Andina, entrada with { Lines = [new InventoryMovementLine(product.Id, 11m, 3m)] }));

        Assert.Equal("inventory_idempotency_conflict", error.Code);
        Assert.Equal(10m, (await StockOfAsync(_ana, product)).QuantityOnHand);
    }

    [Fact]
    public async Task Dos_reintentos_simultaneos_registran_un_solo_movimiento()
    {
        var product = await CreateProductAsync(_ana);
        var warehouse = await DefaultWarehouseAsync(_ana);
        var entrada = Entrada(warehouse.Id, (product.Id, 2m, 1m));

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RegisterAsync(Andina, entrada)));

        Assert.Single(results.Select(r => r.MovementId).Distinct());
        Assert.Single(results, r => !r.AlreadyProcessed);
        Assert.Equal(2m, (await StockOfAsync(_ana, product)).QuantityOnHand);
    }

    // ── Validaciones de ACUERDO-01 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Rechaza_lo_que_ACUERDO_01_no_permite()
    {
        var product = await CreateProductAsync(_ana);
        var service = await CreateProductAsync(_ana, kind: "Service");
        var warehouse = await DefaultWarehouseAsync(_ana);
        var ok = Entrada(warehouse.Id, (product.Id, 1m, 1m));

        Assert.Equal("inventory_product_not_tracked", await RejectedCodeAsync(Andina, Entrada(warehouse.Id, (service.Id, 1m, 1m))));
        Assert.Equal("inventory_product_not_available", await RejectedCodeAsync(Andina, Entrada(warehouse.Id, (Guid.NewGuid(), 1m, 1m))));
        Assert.Equal("inventory_unit_cost_required", await RejectedCodeAsync(Andina, ok with { Lines = [new(product.Id, 1m, null)] }));
        Assert.Equal("inventory_unit_cost_invalid", await RejectedCodeAsync(Andina, ok with { Lines = [new(product.Id, 1m, -1m)] }));
        Assert.Equal("inventory_quantity_invalid", await RejectedCodeAsync(Andina, ok with { Lines = [new(product.Id, 0.00001m, 1m)] }));
        Assert.Equal("inventory_duplicate_product", await RejectedCodeAsync(Andina, ok with { Lines = [new(product.Id, 1m, 1m), new(product.Id, 1m, 1m)] }));
        Assert.Equal("inventory_lines_invalid", await RejectedCodeAsync(Andina, ok with { Lines = [] }));
        Assert.Equal("inventory_unit_cost_not_allowed", await RejectedCodeAsync(Andina,
            Salida(warehouse.Id, (product.Id, 1m)) with { Lines = [new(product.Id, 1m, 3m)] }));
        Assert.Equal("inventory_movement_kind_not_supported", await RejectedCodeAsync(Andina, ok with { Kind = InventoryMovementKind.Ajuste }));
        Assert.Equal("inventory_source_invalid", await RejectedCodeAsync(Andina, ok with { IdempotencyKey = "otra-clave" }));
        Assert.Equal("inventory_source_invalid", await RejectedCodeAsync(Andina, ok with { SourceModule = "sales" }));
    }

    [Fact]
    public async Task Bodega_inexistente_inactiva_o_de_otra_empresa_se_rechaza()
    {
        var product = await CreateProductAsync(_ana);
        var ajena = await ReadAsync<WarehouseResponse>(
            await _bruno.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(NewCode("W"), "Bodega Pacífico"), Ct), HttpStatusCode.Created);
        var inactiva = await ReadAsync<WarehouseResponse>(
            await _ana.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(NewCode("W"), "Bodega cerrada"), Ct), HttpStatusCode.Created);
        await ReadAsync<WarehouseResponse>(await _ana.PatchAsJsonAsync($"/api/v1/warehouses/{inactiva.Id}/status", new ChangeStatusRequest(false), Ct));

        foreach (var warehouseId in new[] { Guid.NewGuid(), ajena.Id, inactiva.Id })
        {
            Assert.Equal("inventory_warehouse_not_available", await RejectedCodeAsync(Andina, Entrada(warehouseId, (product.Id, 1m, 1m))));
        }
    }

    [Fact]
    public async Task Existencias_por_bodega_son_independientes()
    {
        var product = await CreateProductAsync(_ana);
        var principal = await DefaultWarehouseAsync(_ana);
        var norte = await ReadAsync<WarehouseResponse>(
            await _ana.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(NewCode("N"), "Bodega norte"), Ct), HttpStatusCode.Created);

        await RegisterAsync(Andina, Entrada(norte.Id, (product.Id, 3m, 2m)));

        Assert.Equal(0m, (await StockOfAsync(_ana, product, principal.Id)).QuantityOnHand);
        Assert.Equal(3m, (await StockOfAsync(_ana, product, norte.Id)).QuantityOnHand);
        Assert.Equal("inventory_insufficient_stock", await RejectedCodeAsync(Andina, Salida(principal.Id, (product.Id, 1m))));
    }

    // ── API de bodegas y permisos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cada_empresa_tiene_una_sola_bodega_principal_que_no_se_desactiva()
    {
        var first = await DefaultWarehouseAsync(_ana);
        var again = await DefaultWarehouseAsync(_ana);
        var pacifico = await DefaultWarehouseAsync(_bruno);

        var disable = await _ana.PatchAsJsonAsync($"/api/v1/warehouses/{first.Id}/status", new ChangeStatusRequest(false), Ct);

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, pacifico.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, disable.StatusCode);
        Assert.Equal("inventory_default_warehouse_required", await ProblemCodeAsync(disable));
    }

    [Fact]
    public async Task Codigo_de_bodega_duplicado_responde_409_y_la_de_otra_empresa_no_se_ve()
    {
        var code = NewCode("D");
        var created = await ReadAsync<WarehouseResponse>(
            await _ana.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(code, "Bodega uno"), Ct), HttpStatusCode.Created);

        var duplicate = await _ana.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(code.ToLowerInvariant(), "Bodega dos"), Ct);
        var otherTenant = await _bruno.PutAsJsonAsync($"/api/v1/warehouses/{created.Id}", new RenameWarehouseRequest("Ajena"), Ct);
        var stockOther = await _bruno.GetAsync($"/api/v1/stock?warehouseId={created.Id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("inventory_warehouse_code_duplicated", await ProblemCodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stockOther.StatusCode);
    }

    [Fact]
    public async Task Rol_de_consulta_ve_existencias_pero_no_administra_bodegas()
    {
        var carla = _api.ClientFor(DevelopmentSeeder.CarlaEmail, Pacifico);

        var stock = await carla.GetAsync("/api/v1/stock", Ct);
        var create = await carla.PostAsJsonAsync("/api/v1/warehouses", new CreateWarehouseRequest(NewCode("C"), "No permitida"), Ct);

        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Cambio_de_estado_sin_IsActive_responde_400_en_lugar_de_desactivar()
    {
        var product = await CreateProductAsync(_ana);

        var response = await _ana.PatchAsync($"/api/v1/products/{product.Id}/status", JsonContent.Create(new { }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await _ana.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}", Ct))!.IsActive);
    }
}
