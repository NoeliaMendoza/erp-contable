using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Sales.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class SalesApiTests(PostgresFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("customerId")]
    [InlineData("idempotencyKey")]
    [InlineData("warehouseId")]
    [InlineData("lines")]
    [InlineData("productId")]
    [InlineData("quantity")]
    public async Task Confirmar_exige_campos_obligatorios(string missing)
    {
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        var line = new JsonObject
        {
            ["productId"] = Guid.NewGuid(),
            ["quantity"] = 1m,
        };
        var request = new JsonObject
        {
            ["customerId"] = Guid.NewGuid(),
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["warehouseId"] = Guid.NewGuid(),
            ["lines"] = new JsonArray(line),
        };
        Assert.True(request.Remove(missing) || line.Remove(missing));

        using var response = await andina.PostAsJsonAsync("/api/v1/sales/confirm", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirmar_descuenta_stock_contabiliza_costo_reintenta_sin_duplicar_lineas_y_lista_venta()
    {
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        using var pacifico = api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        var (product, warehouse) = await CreateStockedProductAsync(api, andina);
        var customer = await CreateCustomerAsync(andina);
        var request = new ConfirmSaleRequest(customer.Id, new DateOnly(2026, 11, 27),
            $"sales-test:{Guid.NewGuid():D}", warehouse.Id,
            [new SaleLineRequest(product.Id, 4m)]);

        using var response = await andina.PostAsJsonAsync("/api/v1/sales/confirm", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sale = (await response.Content.ReadFromJsonAsync<SaleResponse>(Ct))!;
        Assert.Equal("Confirmed", sale.Status);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal(customer.Name, sale.CustomerName);
        Assert.Equal(20m, sale.Total);
        Assert.Single(sale.Lines);
        Assert.NotNull(sale.StockMovementId);
        Assert.NotNull(sale.JournalEntryId);

        using var retryResponse = await andina.PostAsJsonAsync("/api/v1/sales/confirm", request, Ct);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retry = (await retryResponse.Content.ReadFromJsonAsync<SaleResponse>(Ct))!;
        Assert.Equal(sale.Id, retry.Id);
        Assert.Equal(sale.StockMovementId, retry.StockMovementId);
        Assert.Equal(sale.JournalEntryId, retry.JournalEntryId);
        Assert.Single(retry.Lines);

        var stock = await andina.GetFromJsonAsync<StockReportResponse>(
            $"/api/v1/stock?search={product.Sku}&warehouseId={warehouse.Id:D}", Ct);
        Assert.Equal(6m, Assert.Single(stock!.Items.Items).QuantityOnHand);
        await AssertPostedOnceAsync(sale, product, warehouse);

        var history = await andina.GetFromJsonAsync<PagedResult<SaleResponse>>(
            $"/api/v1/sales?search={Uri.EscapeDataString(sale.Number)}", Ct);
        Assert.Equal(sale.Id, Assert.Single(history!.Items).Id);
        using var foreignRead = await pacifico.GetAsync($"/api/v1/sales/{sale.Id:D}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
    }

    [Fact]
    public async Task Confirmar_rechaza_cliente_de_otra_empresa()
    {
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        using var pacifico = api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        var (product, warehouse) = await CreateStockedProductAsync(api, andina);
        var foreignCustomer = await CreateCustomerAsync(pacifico);
        var request = new ConfirmSaleRequest(foreignCustomer.Id, null, $"foreign:{Guid.NewGuid():D}",
            warehouse.Id, [new SaleLineRequest(product.Id, 1m)]);

        using var response = await andina.PostAsJsonAsync("/api/v1/sales/confirm", request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmar_rechaza_cliente_inactivo()
    {
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        var (product, warehouse) = await CreateStockedProductAsync(api, andina);
        var customer = await CreateCustomerAsync(andina);
        using var deactivated = await andina.PatchAsJsonAsync($"/api/v1/customers/{customer.Id:D}/status",
            new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var request = new ConfirmSaleRequest(customer.Id, null, $"inactive:{Guid.NewGuid():D}",
            warehouse.Id, [new SaleLineRequest(product.Id, 1m)]);

        using var response = await andina.PostAsJsonAsync("/api/v1/sales/confirm", request, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(Ct);
        Assert.Equal("sale_customer_inactive", problem!["code"].GetString());
    }

    private static async Task<CustomerResponse> CreateCustomerAsync(HttpClient client)
    {
        var number = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        using var response = await client.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Cedula", number, "Cliente de venta", null, null, null), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;
    }

    private static async Task<(ProductResponse Product, WarehouseResponse Warehouse)> CreateStockedProductAsync(
        ErpApiFactory api, HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var unitResponse = await client.PostAsJsonAsync("/api/v1/units",
            new SaveUnitRequest($"U-{suffix}", "Unidad de prueba"), Ct);
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        var unit = (await unitResponse.Content.ReadFromJsonAsync<UnitResponse>(Ct))!;
        using var productResponse = await client.PostAsJsonAsync("/api/v1/products",
            new SaveProductRequest($"P-{suffix}", "Producto de venta", "Product", unit.Id, 5m,
                "ZeroRated", Cost: 3m), Ct);
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>(Ct))!;
        using var warehouseResponse = await client.GetAsync("/api/v1/warehouses", Ct);
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        var warehouse = (await warehouseResponse.Content.ReadFromJsonAsync<List<WarehouseResponse>>(Ct))!
            .Single(w => w.IsDefault);

        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>()
            .ConfirmTenant(DevelopmentSeeder.TenantAndinaId, []);
        var movement = scope.ServiceProvider.GetRequiredService<IInventoryMovementService>();
        var receiptId = Guid.CreateVersion7();
        await movement.RegisterAsync(new InventoryMovementRequest(
            $"purchases:receipt:{receiptId:D}", "purchases", "receipt", receiptId,
            warehouse.Id, InventoryMovementKind.Entrada, new DateOnly(2026, 11, 27),
            [new InventoryMovementLine(product.Id, 10m, 3m)]), Ct);
        return (product, warehouse);
    }

    private async Task AssertPostedOnceAsync(
        SaleResponse sale, ProductResponse product, WarehouseResponse warehouse)
    {
        await using var connection = new NpgsqlConnection(db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var tenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction))
        {
            tenant.Parameters.AddWithValue("tenant", DevelopmentSeeder.TenantAndinaId.ToString());
            await tenant.ExecuteNonQueryAsync(Ct);
        }

        await using var stock = new NpgsqlCommand("""
            SELECT quantity_on_hand FROM inventory.stock_balances
            WHERE product_id = @product AND warehouse_id = @warehouse;
            """, connection, transaction);
        stock.Parameters.AddWithValue("product", product.Id);
        stock.Parameters.AddWithValue("warehouse", warehouse.Id);
        Assert.Equal(6m, (decimal)(await stock.ExecuteScalarAsync(Ct))!);

        await using var movement = new NpgsqlCommand("""
            SELECT count(*), max(total_cost) FROM inventory.inventory_movements
            WHERE source_module = 'sales' AND source_document_id = @sale;
            """, connection, transaction);
        movement.Parameters.AddWithValue("sale", sale.Id);
        await using (var reader = await movement.ExecuteReaderAsync(Ct))
        {
            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(12m, reader.GetDecimal(1));
        }

        await using var posting = new NpgsqlCommand("""
            SELECT count(*) FROM accounting.journal_entries
            WHERE source_module = 'sales' AND source_document_id = @sale;
            """, connection, transaction);
        posting.Parameters.AddWithValue("sale", sale.Id);
        Assert.Equal(1L, (long)(await posting.ExecuteScalarAsync(Ct))!);

        await using var lines = new NpgsqlCommand("""
            SELECT sum(debit), sum(credit), count(*) FROM accounting.journal_entry_lines
            WHERE journal_entry_id = @entry;
            """, connection, transaction);
        lines.Parameters.AddWithValue("entry", sale.JournalEntryId!.Value);
        await using (var lineReader = await lines.ExecuteReaderAsync(Ct))
        {
            Assert.True(await lineReader.ReadAsync(Ct));
            Assert.Equal(32m, lineReader.GetDecimal(0));
            Assert.Equal(32m, lineReader.GetDecimal(1));
            Assert.Equal(4L, lineReader.GetInt64(2));
        }

        await using var cost = new NpgsqlCommand("""
            SELECT debit FROM accounting.journal_entry_lines
            WHERE journal_entry_id = @entry AND account_code = 'Costo de ventas';
            """, connection, transaction);
        cost.Parameters.AddWithValue("entry", sale.JournalEntryId.Value);
        Assert.Equal(12m, (decimal)(await cost.ExecuteScalarAsync(Ct))!);
    }
}
