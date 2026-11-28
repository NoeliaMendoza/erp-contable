using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Purchases.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace ERP.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class PurchasesApiTests(PostgresFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("supplierId")]
    [InlineData("lines")]
    [InlineData("productId")]
    [InlineData("quantity")]
    [InlineData("unitCost")]
    public async Task Registrar_exige_todos_los_campos_obligatorios_del_json(string missing)
    {
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        var line = new JsonObject
        {
            ["productId"] = Guid.NewGuid(),
            ["quantity"] = 10m,
            ["unitCost"] = 3m,
        };
        var request = new JsonObject
        {
            ["supplierId"] = Guid.NewGuid(),
            ["lines"] = new JsonArray(line),
        };
        Assert.True(request.Remove(missing) || line.Remove(missing));

        using var response = await andina.PostAsJsonAsync("/api/v1/purchases", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        var jsonPath = missing is "supplierId" or "lines" ? "$" : "$.lines[0]";
        var errors = Assert.IsType<JsonArray>(problem?["errors"]?[jsonPath]);
        Assert.Contains(missing, Assert.Single(errors)!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registrar_una_compra_con_una_linea_nula_responde_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);

        using var response = await andina.PostAsJsonAsync("/api/v1/purchases",
            new { supplierId = Guid.NewGuid(), lines = new object?[] { null } }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registra_y_consulta_el_borrador_solo_en_la_empresa_activa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new ErpApiFactory(db);
        using var andina = api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        using var pacifico = api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        var products = await andina.GetFromJsonAsync<PagedResult<ProductResponse>>("/api/v1/products?search=CUAD-001", ct);
        var notebook = Assert.Single(products!.Items, product => product.Sku == "CUAD-001");
        using var supplierResponse = await andina.PostAsJsonAsync("/api/v1/suppliers",
            new SaveSupplierRequest("06", Guid.NewGuid().ToString("N")[..16], "Proveedor de cuadernos", null, null, null), ct);
        Assert.Equal(HttpStatusCode.Created, supplierResponse.StatusCode);
        var supplier = (await supplierResponse.Content.ReadFromJsonAsync<SupplierResponse>(ct))!;

        using var response = await andina.PostAsJsonAsync("/api/v1/purchases",
            new RegisterPurchaseRequest(supplier.Id, null, [new RegisterPurchaseLineRequest(notebook.Id, 10m, 3m)]), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = (await response.Content.ReadFromJsonAsync<PurchaseResponse>(ct))!;
        Assert.Equal("Draft", purchase.Status);
        Assert.Equal(30m, purchase.TotalCost);
        Assert.Null(purchase.StockMovementId);
        Assert.Null(purchase.JournalEntryId);
        var line = Assert.Single(purchase.Lines);
        Assert.Equal(10m, line.Quantity);
        Assert.Equal(3m, line.UnitCost);

        var path = $"/api/v1/purchases/{purchase.Id}";
        var saved = await andina.GetFromJsonAsync<PurchaseResponse>(path, ct);
        Assert.Equal(purchase.Id, saved!.Id);
        var search = $"/api/v1/purchases?search={Uri.EscapeDataString(purchase.Number)}";
        var ownPage = await andina.GetFromJsonAsync<PagedResult<PurchaseResponse>>(search, ct);
        Assert.Contains(ownPage!.Items, item => item.Id == purchase.Id);
        var otherPage = await pacifico.GetFromJsonAsync<PagedResult<PurchaseResponse>>(search, ct);
        Assert.DoesNotContain(otherPage!.Items, item => item.Id == purchase.Id);
        using var otherResponse = await pacifico.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task Confirmar_en_paralelo_registra_una_sola_entrada_y_un_solo_asiento()
    {
        await using var api = new ErpApiFactory(db);
        var inventoryCode = $"INV-{Guid.NewGuid():N}"[..24];
        var cashCode = $"CAJA-{Guid.NewGuid():N}"[..24];
        await using var configured = WithAccounts(api, inventoryCode, cashCode);
        using var andina = configured.CreateClient();
        SetAndinaHeaders(andina);
        using var pacifico = api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        await CreateAccountAsync(andina, inventoryCode);
        await CreateAccountAsync(andina, cashCode);
        var (purchase, product) = await CreateDraftAsync(andina);
        var path = $"/api/v1/purchases/{purchase.Id}/confirm";

        using var foreign = await pacifico.PostAsync(path, null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var confirmations = await Task.WhenAll(ConfirmAsync(andina, path), ConfirmAsync(andina, path));
        var confirmed = confirmations[0];
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(30m, confirmed.TotalCost);
        Assert.NotNull(confirmed.StockMovementId);
        Assert.NotNull(confirmed.JournalEntryId);
        Assert.Equal(confirmed.StockMovementId, confirmations[1].StockMovementId);
        Assert.Equal(confirmed.JournalEntryId, confirmations[1].JournalEntryId);
        var retried = await ConfirmAsync(andina, path);
        Assert.Equal(confirmed.StockMovementId, retried.StockMovementId);
        Assert.Equal(confirmed.JournalEntryId, retried.JournalEntryId);
        await AssertReceiptAsync(purchase, product, confirmed, inventoryCode, cashCode);
    }

    [Theory]
    [InlineData(true, "inventory_product_inactive")]
    [InlineData(false, "posting_account_not_available")]
    public async Task Un_fallo_deja_el_borrador_sin_entrada_ni_asiento(bool failStock, string errorCode)
    {
        await using var api = new ErpApiFactory(db);
        var inventoryCode = $"INV-{Guid.NewGuid():N}"[..24];
        var cashCode = $"CAJA-{Guid.NewGuid():N}"[..24];
        await using var configured = WithAccounts(api, inventoryCode, cashCode);
        using var andina = configured.CreateClient();
        SetAndinaHeaders(andina);
        await CreateAccountAsync(andina, inventoryCode);
        var (purchase, product) = await CreateDraftAsync(andina);
        if (failStock)
        {
            await CreateAccountAsync(andina, cashCode);
            using var disabled = await andina.PatchAsJsonAsync($"/api/v1/products/{product.Id}/status", new { isActive = false }, Ct);
            disabled.EnsureSuccessStatusCode();
        }

        var path = $"/api/v1/purchases/{purchase.Id}";
        using var rejected = await andina.PostAsync($"{path}/confirm", null, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(Ct);
        Assert.Equal(errorCode, problem!["code"].GetString());
        var draft = await andina.GetFromJsonAsync<PurchaseResponse>(path, Ct);
        Assert.Equal("Draft", draft!.Status);
        Assert.Null(draft.StockMovementId);
        Assert.Null(draft.JournalEntryId);
        await AssertReceiptAsync(purchase, product);

        if (!failStock)
        {
            await CreateAccountAsync(andina, cashCode);
            var confirmed = await ConfirmAsync(andina, $"{path}/confirm");
            await AssertReceiptAsync(purchase, product, confirmed, inventoryCode, cashCode);
        }
    }

    private static WebApplicationFactory<Program> WithAccounts(
        ErpApiFactory api, string inventoryCode, string cashCode) =>
        api.WithWebHostBuilder(builder =>
        {
            var prefix = $"Purchases:PostingAccounts:{DevelopmentSeeder.TenantAndinaId:D}";
            builder.UseSetting($"{prefix}:Inventory", inventoryCode);
            builder.UseSetting($"{prefix}:Cash", cashCode);
        });

    private static void SetAndinaHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(ERP.Api.Security.AuthenticationSetup.DevelopmentUserHeader, DevelopmentSeeder.AnaEmail);
        client.DefaultRequestHeaders.Add(ERP.Api.Security.TenantResolutionMiddleware.TenantHeader, DevelopmentSeeder.TenantAndinaId.ToString());
    }

    private static async Task CreateAccountAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/accounts",
            new SaveAccountRequest(code, code, "Asset", "Debit", null, true), Ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(PurchaseResponse Purchase, ProductResponse Product)> CreateDraftAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<PagedResult<ProductResponse>>("/api/v1/products?search=CUAD-001", Ct);
        var notebook = Assert.Single(products!.Items, product => product.Sku == "CUAD-001");
        using var productResponse = await client.PostAsJsonAsync("/api/v1/products",
            new SaveProductRequest($"CUAD-{Guid.NewGuid():N}"[..24], "Cuaderno para recepción", "Product", notebook.UnitId, 5m, "ZeroRated"), Ct);
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>(Ct))!;
        using var supplierResponse = await client.PostAsJsonAsync("/api/v1/suppliers",
            new SaveSupplierRequest("06", Guid.NewGuid().ToString("N")[..16], "Proveedor de cuadernos", null, null, null), Ct);
        supplierResponse.EnsureSuccessStatusCode();
        var supplier = (await supplierResponse.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!;
        using var response = await client.PostAsJsonAsync("/api/v1/purchases",
            new RegisterPurchaseRequest(supplier.Id, null, [new RegisterPurchaseLineRequest(product.Id, 10m, 3m)]), Ct);
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<PurchaseResponse>(Ct))!, product);
    }

    private static async Task<PurchaseResponse> ConfirmAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsync(path, null, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PurchaseResponse>(Ct))!;
    }

    private async Task AssertReceiptAsync(PurchaseResponse purchase, ProductResponse product,
        PurchaseResponse? confirmed = null, string? inventoryCode = null, string? cashCode = null)
    {
        await using var connection = new NpgsqlConnection(db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using var tenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        tenant.Parameters.AddWithValue("tenant", DevelopmentSeeder.TenantAndinaId.ToString());
        await tenant.ExecuteNonQueryAsync(Ct);
        await using var command = new NpgsqlCommand("""
            SELECT quantity_on_hand, average_cost FROM inventory.stock_balances
            WHERE product_id = @product AND warehouse_id = @warehouse;
            SELECT id, total_cost FROM inventory.inventory_movements
            WHERE source_module = 'purchases' AND source_document_type = 'receipt' AND source_document_id = @purchase;
            SELECT e.id, l.account_code, l.debit, l.credit
            FROM accounting.journal_entries e JOIN accounting.journal_entry_lines l ON l.journal_entry_id = e.id
            WHERE e.source_module = 'purchases' AND e.source_document_type = 'receipt' AND e.source_document_id = @purchase
            ORDER BY l.debit DESC;
            """, connection, transaction);
        command.Parameters.AddWithValue("product", product.Id);
        command.Parameters.AddWithValue("warehouse", purchase.WarehouseId);
        command.Parameters.AddWithValue("purchase", purchase.Id);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        if (confirmed is null)
        {
            Assert.False(await reader.ReadAsync(Ct));
            Assert.True(await reader.NextResultAsync(Ct));
            Assert.False(await reader.ReadAsync(Ct));
            Assert.True(await reader.NextResultAsync(Ct));
            Assert.False(await reader.ReadAsync(Ct));
            return;
        }

        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(10m, reader.GetDecimal(0));
        Assert.Equal(3m, reader.GetDecimal(1));
        Assert.False(await reader.ReadAsync(Ct));
        Assert.True(await reader.NextResultAsync(Ct));
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(confirmed.StockMovementId, reader.GetGuid(0));
        Assert.Equal(30m, reader.GetDecimal(1));
        Assert.False(await reader.ReadAsync(Ct));
        Assert.True(await reader.NextResultAsync(Ct));
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal((confirmed.JournalEntryId, inventoryCode?.ToUpperInvariant(), 30m, 0m),
            ((Guid?)reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal((confirmed.JournalEntryId, cashCode?.ToUpperInvariant(), 0m, 30m),
            ((Guid?)reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        Assert.False(await reader.ReadAsync(Ct));
    }
}
