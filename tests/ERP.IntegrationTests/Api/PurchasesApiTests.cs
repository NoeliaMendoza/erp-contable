using System.Net;
using System.Net.Http.Json;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Purchases.Dtos;

namespace ERP.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class PurchasesApiTests(PostgresFixture db)
{
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
}
