using System.Net;
using System.Text;
using System.Text.Json;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Sales;
using ERP.Frontend.Modules.Sales.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class SalesFrontendTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid MainWarehouseId = Guid.NewGuid();
    private static readonly Guid SecondaryWarehouseId = Guid.NewGuid();

    private static readonly ProductItem Product = new(ProductId, "CUAD-001", "Cuaderno", "Product",
        Guid.NewGuid(), "UND", "Unidad", 5m, true, "ZeroRated", null, null, null, null, 3m, true, null);
    private static readonly CustomerItem Customer = new(CustomerId, "Cedula", "1234567890", "Cliente",
        null, null, null, true, null);
    private static readonly List<WarehouseItem> Warehouses =
    [
        new(MainWarehouseId, "MAIN", "Principal", true, true),
        new(SecondaryWarehouseId, "SECOND", "Secundaria", false, true),
        new(Guid.NewGuid(), "INACTIVE", "Inactiva", false, false),
    ];

    [Fact]
    public async Task Inicializa_catalogos_historico_y_paginacion()
    {
        var (vm, requests) = CreateViewModel(
            ["sales.invoices.read", "sales.invoices.issue"], out _);
        await WaitForIdleAsync(vm);

        Assert.True(vm.CanConfirm);
        Assert.True(vm.CanRead);
        Assert.Equal(ProductId, Assert.Single(vm.Products).Id);
        Assert.Equal(CustomerId, Assert.Single(vm.Customers).Id);
        Assert.Equal("Cliente — Cedula 1234567890", Customer.Display);
        Assert.Equal(2, vm.Warehouses.Count);
        Assert.Equal(MainWarehouseId, vm.SelectedWarehouse!.Id);
        Assert.Equal(3, vm.SalesTotalPages);
        Assert.Equal("Página 1 de 3", vm.SalesPageLabel);
        Assert.False(vm.CanGoPrevious);
        Assert.True(vm.CanGoNext);
        Assert.Single(vm.Sales);

        await vm.NextSalesPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SalesPage);
        Assert.True(vm.CanGoPrevious);
        Assert.Contains(requests, request =>
            request.RequestUri!.PathAndQuery == "/api/v1/sales?page=2&pageSize=20");

        await vm.NextSalesPageCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.SalesPage);
        Assert.False(vm.CanGoNext);
        await vm.NextSalesPageCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.SalesPage);

        await vm.PreviousSalesPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SalesPage);
    }

    [Fact]
    public async Task SalesApi_busca_ventas_con_el_texto_codificado()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(Page<SaleResponse>([], 0)));
        });
        var api = new SalesApi(new ErpApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }));

        await api.ListSalesAsync(2, 10, " V-001 ", TestContext.Current.CancellationToken);

        Assert.Equal("/api/v1/sales?page=2&pageSize=10&search=V-001",
            Assert.Single(requests).RequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SalesApi_omite_busqueda_vacía(string? search)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(Page<SaleResponse>([], 0)));
        });
        var api = new SalesApi(new ErpApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }));

        await api.ListSalesAsync(1, 20, search, TestContext.Current.CancellationToken);

        Assert.Equal("/api/v1/sales?page=1&pageSize=20", Assert.Single(requests).RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Confirmar_envia_cliente_producto_y_bodega_y_muestra_venta()
    {
        var (vm, requests) = CreateViewModel(
            ["sales.invoices.read", "sales.invoices.issue"], out _);
        await WaitForIdleAsync(vm);
        vm.SelectedCustomer = Customer;
        vm.SelectedProduct = Product;
        vm.SelectedWarehouse = Warehouses[1];

        vm.CanSubmit = false;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
        vm.CanSubmit = true;
        await vm.ConfirmCommand.ExecuteAsync(null);

        var post = Assert.Single(requests, request => request.Method == HttpMethod.Post);
        Assert.Equal("/api/v1/sales/confirm", post.RequestUri!.AbsolutePath);
        using var requestBody = JsonDocument.Parse(await post.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(CustomerId, requestBody.RootElement.GetProperty("customerId").GetGuid());
        Assert.Equal(SecondaryWarehouseId, requestBody.RootElement.GetProperty("warehouseId").GetGuid());
        var line = Assert.Single(requestBody.RootElement.GetProperty("lines").EnumerateArray());
        Assert.Equal(ProductId, line.GetProperty("productId").GetGuid());
        Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal("Confirmed", vm.LastSale!.Status);
        Assert.Contains(vm.LastSale.Number, vm.SuccessMessage);
        Assert.Equal(1, vm.SalesPage);

        vm.ResetCommand.Execute(null);
        Assert.Null(vm.SelectedCustomer);
        Assert.Null(vm.SelectedProduct);
        Assert.Equal(MainWarehouseId, vm.SelectedWarehouse!.Id);
        Assert.Equal("1", vm.QuantityText);
        Assert.Null(vm.LastSale);
    }

    [Fact]
    public async Task Validaciones_y_error_del_servidor_se_muestran_sin_perder_el_reintento()
    {
        var (vm, requests) = CreateViewModel(
            ["sales.invoices.read", "sales.invoices.issue"], out _, failConfirmation: true);
        await WaitForIdleAsync(vm);

        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Seleccione un cliente registrado.", vm.ErrorMessage);
        vm.SelectedCustomer = Customer;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Seleccione un producto o servicio.", vm.ErrorMessage);
        vm.SelectedProduct = Product;
        vm.QuantityText = "0";
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Ingrese una cantidad válida mayor que cero.", vm.ErrorMessage);
        vm.QuantityText = "1";
        vm.SelectedWarehouse = null;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Seleccione una bodega activa.", vm.ErrorMessage);

        vm.SelectedWarehouse = Warehouses[0];
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("No se pudo confirmar la venta.", vm.ErrorMessage);
        Assert.True(vm.CanSubmit);
        Assert.Null(vm.LastSale);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var retries = requests.Where(request => request.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, retries.Length);
        using var firstBody = JsonDocument.Parse(await retries[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var retryBody = JsonDocument.Parse(await retries[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(firstBody.RootElement.GetProperty("idempotencyKey").GetString(),
            retryBody.RootElement.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task Sin_permisos_no_muestra_ventas_ni_consulta_el_historial()
    {
        var (vm, requests) = CreateViewModel([], out _);
        await WaitForIdleAsync(vm);

        Assert.False(vm.CanConfirm);
        Assert.False(vm.CanRead);
        Assert.DoesNotContain(requests, request =>
            request.RequestUri!.PathAndQuery.StartsWith("/api/v1/sales?", StringComparison.Ordinal));
    }

    private static (SalesViewModel ViewModel, List<HttpRequestMessage> Requests) CreateViewModel(
        IReadOnlyList<string> permissions,
        out SessionState session,
        bool failConfirmation = false)
    {
        session = new SessionState();
        session.SignIn("sales@example.test");
        session.SelectTenant(Guid.NewGuid(), "Empresa de prueba");
        session.SetTenantDetails(permissions, ["sales", "inventory"]);
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler(request =>
        {
            lock (requests)
            {
                requests.Add(request);
            }

            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/v1/sales/confirm")
            {
                return failConfirmation
                    ? Json(HttpStatusCode.UnprocessableEntity, """{"detail":"No se pudo confirmar la venta."}""")
                    : Json(HttpStatusCode.OK, JsonSerializer.Serialize(ConfirmedSale()));
            }

            return path switch
            {
                "/api/v1/products" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(Page([Product], 1))),
                "/api/v1/customers" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(Page([Customer], 1))),
                "/api/v1/warehouses" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(Warehouses)),
                "/api/v1/sales" => Json(HttpStatusCode.OK,
                    JsonSerializer.Serialize(Page([ConfirmedSale()], 41))),
                _ => Json(HttpStatusCode.NotFound, """{"detail":"Ruta de prueba no esperada."}"""),
            };
        });
        var api = new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
        return (new SalesViewModel(new SalesApi(api), new InventoryApi(api), session), requests);
    }

    private static PagedResult<T> Page<T>(IReadOnlyList<T> items, int totalCount) =>
        new(items, totalCount, 1, 20);

    private static SaleResponse ConfirmedSale() =>
        new(Guid.NewGuid(), "V-TEST", Customer.Name, CustomerId, new DateOnly(2026, 11, 28),
            "Confirmed", 5m, 0m, 5m, "NotRequested", null, Guid.NewGuid(), []);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task WaitForIdleAsync(ViewModelBase viewModel)
    {
        for (var i = 0; i < 100 && viewModel.IsBusy; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
