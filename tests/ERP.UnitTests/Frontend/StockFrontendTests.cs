using System.Net;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Inventory.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;
using static ERP.UnitTests.Frontend.InventoryFrontendTests;

namespace ERP.UnitTests.Frontend;

/// <summary>Pantalla de existencias y kardex (INV-03) contra una API simulada.</summary>
public sealed class StockFrontendTests
{
    private static readonly Guid Principal = Guid.Parse("0195a000-0000-7000-8000-00000000f001");
    private static readonly Guid Norte = Guid.Parse("0195a000-0000-7000-8000-00000000f002");
    private static readonly Guid Cuaderno = Guid.Parse("0195a000-0000-7000-8000-0000000000c1");

    private static readonly string Warehouses = $$"""
        [{"id":"{{Principal}}","code":"PRINCIPAL","name":"Bodega principal","isDefault":true,"isActive":true},
         {"id":"{{Norte}}","code":"NORTE","name":"Bodega norte","isDefault":false,"isActive":true}]
        """;

    private static string Stock(Guid warehouseId, int totalCount = 1) => $$"""
        {"warehouse":{"id":"{{warehouseId}}","code":"X","name":"Bodega","isDefault":true,"isActive":true},
         "items":{"items":[{"productId":"{{Cuaderno}}","sku":"CUAD-001","name":"Cuaderno","unitCode":"UND","isActive":true,
           "quantityOnHand":6,"quantityReserved":0,"available":6,"averageCost":3,"totalValue":18,"minStock":10,"belowMinimum":true}],
         "totalCount":{{totalCount}},"page":1,"pageSize":20} }
        """;

    private static readonly string KardexJson = $$"""
        {"productId":"{{Cuaderno}}","sku":"CUAD-001","name":"Cuaderno","unitCode":"UND",
         "warehouse":{"id":"{{Principal}}","code":"PRINCIPAL","name":"Bodega principal","isDefault":true,"isActive":true},
         "quantityOnHand":6,"averageCost":3,
         "entries":{"items":[
           {"movementId":"{{Guid.Empty}}","sequence":1,"movementDate":"2026-11-20","registeredAtUtc":"2026-11-20T10:00:00Z","kind":"Entrada",
            "sourceModule":"purchases","sourceDocumentType":"receipt","sourceDocumentId":"{{Guid.Empty}}","quantityIn":10,"quantityOut":0,
            "unitCost":3,"lineCost":30,"quantityAfter":10,"averageCostAfter":3},
           {"movementId":"{{Guid.Empty}}","sequence":2,"movementDate":"2026-11-20","registeredAtUtc":"2026-11-20T11:00:00Z","kind":"Salida",
            "sourceModule":"sales","sourceDocumentType":"invoice","sourceDocumentId":"{{Guid.Empty}}","quantityIn":0,"quantityOut":4,
            "unitCost":3,"lineCost":12,"quantityAfter":6,"averageCostAfter":3}],
         "totalCount":2,"page":1,"pageSize":50} }
        """;

    [Fact]
    public async Task Abre_en_la_bodega_principal_con_sus_existencias()
    {
        var (vm, requests) = await CreateAsync();

        Assert.Equal(Principal, vm.SelectedWarehouse!.Id);
        Assert.Equal(2, vm.Warehouses.Count);
        Assert.Contains(requests, r => r.Contains("api/v1/stock?", StringComparison.Ordinal) && r.Contains($"warehouseId={Principal}", StringComparison.Ordinal));
        var item = Assert.Single(vm.Items);
        Assert.Equal("6 UND", item.QuantityLabel);
        Assert.Equal("Bajo el mínimo", item.Alert);
        Assert.Contains("18", vm.TotalValueLabel, StringComparison.Ordinal);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Kardex_muestra_entradas_y_salidas_con_su_saldo()
    {
        var (vm, _) = await CreateAsync();

        await vm.ShowKardexCommand.ExecuteAsync(vm.Items[0]);

        Assert.True(vm.HasKardex);
        Assert.Collection(vm.KardexEntries,
            e => Assert.Equal(("Compra", "10", "", "10", "3.0000"), (e.Source, e.InLabel, e.OutLabel, e.BalanceLabel, e.AverageLabel.Replace(',', '.'))),
            e => Assert.Equal(("Venta", "", "4", "6"), (e.Source, e.InLabel, e.OutLabel, e.BalanceLabel)));

        vm.CloseKardexCommand.Execute(null);

        Assert.False(vm.HasKardex);
        Assert.Empty(vm.KardexEntries);
    }

    [Fact]
    public async Task Cambiar_de_bodega_recarga_y_cierra_el_kardex()
    {
        var (vm, requests) = await CreateAsync();
        await vm.ShowKardexCommand.ExecuteAsync(vm.Items[0]);

        vm.SelectedWarehouse = vm.Warehouses.Single(w => w.Id == Norte);
        await WaitForIdleAsync(vm);

        Assert.False(vm.HasKardex);
        Assert.Contains(requests, r => r.Contains($"warehouseId={Norte}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Paginacion_avanza_retrocede_y_la_busqueda_vuelve_a_la_primera()
    {
        var (vm, requests) = await CreateAsync(totalCount: 45);
        Assert.Equal(3, vm.TotalPages);
        Assert.False(vm.CanGoPrevious);

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Page);
        Assert.True(vm.CanGoPrevious);

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Page);
        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Page);

        await vm.NextPageCommand.ExecuteAsync(null);
        vm.Search = "cuad";
        await vm.SearchNowCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Page);
        Assert.Contains(requests, r => r.Contains("search=cuad", StringComparison.Ordinal) && r.Contains("page=1&", StringComparison.Ordinal));

        vm.ClearSearchCommand.Execute(null);
        Assert.Null(vm.Search);
    }

    [Fact]
    public void Etiquetas_de_bodegas_y_kardex()
    {
        Assert.Equal("Principal (principal)", new WarehouseItem(Guid.Empty, "P", "Principal", true, true).Display);
        Assert.Equal("Norte (inactiva)", new WarehouseItem(Guid.Empty, "N", "Norte", false, false).Display);
        Assert.Equal("Norte", new WarehouseItem(Guid.Empty, "N", "Norte", false, true).Display);

        var sinMinimo = new StockItem(Guid.Empty, "S", "Sin mínimo", "UND", true, 1, 0, 1, 1, 1, null, false);
        Assert.Equal("—", sinMinimo.MinimumLabel);
        Assert.Equal(string.Empty, sinMinimo.Alert);

        var antiguo = new KardexEntry(Guid.Empty, 1, default, default, "Salida", "otro", "x", Guid.Empty, 0, 1, null, 0, null, null);
        Assert.Equal(("otro", "—", "—"), (antiguo.Source, antiguo.BalanceLabel, antiguo.AverageLabel));
    }

    private static async Task<(StockViewModel Vm, List<string> Requests)> CreateAsync(int totalCount = 1)
    {
        var session = new SessionState();
        session.SignIn("ana.admin@demo.local");
        session.SelectTenant(Guid.NewGuid(), "Demo");
        session.SetTenantDetails(["inventory.kardex.read"], ["inventory"]);

        var requests = new List<string>();
        var handler = new SessionHeadersHandler(session)
        {
            InnerHandler = new StubHandler(req =>
            {
                var url = req.RequestUri!.PathAndQuery;
                lock (requests)
                {
                    requests.Add(url);
                }

                if (url.Contains("/kardex", StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.OK, KardexJson);
                }

                return url.Contains("api/v1/warehouses", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.OK, Warehouses)
                    : Json(HttpStatusCode.OK, Stock(url.Contains(Norte.ToString(), StringComparison.Ordinal) ? Norte : Principal, totalCount));
            }),
        };
        var api = new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
        var vm = new StockViewModel(new InventoryApi(api));
        for (var i = 0; i < 100 && vm.Items.Count == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await WaitForIdleAsync(vm);
        return (vm, requests);
    }

    private static async Task WaitForIdleAsync(StockViewModel vm)
    {
        for (var i = 0; i < 100 && vm.IsBusy; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
