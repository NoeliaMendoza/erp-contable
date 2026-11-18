using System.Net;
using System.Text;
using ERP.Frontend.Modules.Inventory;
using ERP.Frontend.Modules.Inventory.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class InventoryFrontendTests
{
    private const string EmptyPage = """{"items":[],"totalCount":0,"page":1,"pageSize":20}""";

    [Theory]
    [InlineData("5,25", 5.25)]
    [InlineData("5.25", 5.25)]
    [InlineData(" 10 ", 10)]
    [InlineData("0,0001", 0.0001)]
    public void Precio_acepta_coma_o_punto(string text, double expected)
    {
        Assert.True(PriceParser.TryParse(text, out var price));
        Assert.Equal((decimal)expected, price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.234,50")]
    public void Precio_invalido_se_rechaza(string text)
    {
        Assert.False(PriceParser.TryParse(text, out _));
    }

    [Fact]
    public void Consulta_codifica_busqueda_y_filtros()
    {
        var query = InventoryApi.Query(2, 20, " cuad & 1 ", isActive: true, kind: "Service");

        Assert.Equal("page=2&pageSize=20&search=cuad%20%26%201&isActive=true&kind=Service", query);
    }

    [Fact]
    public async Task Formulario_no_envia_un_precio_invalido()
    {
        var requests = new List<HttpRequestMessage>();
        var vm = CreateViewModel(requests, _ => Json(HttpStatusCode.OK, EmptyPage));
        await WaitForIdleAsync(vm);
        requests.Clear();

        vm.Sku = "X1";
        vm.Name = "Artículo";
        vm.FormUnit = new UnitItem(Guid.NewGuid(), "UND", "Unidad", true);
        vm.PriceText = "cinco";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PriceError);
        Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Errores_de_validacion_de_la_api_se_muestran_junto_al_campo()
    {
        var vm = CreateViewModel([], req => req.Method == HttpMethod.Post
            ? Json(HttpStatusCode.BadRequest, """{"status":400,"title":"Datos inválidos","errors":{"Sku":["El SKU solo admite letras, números, punto, guion y guion bajo."]}}""")
            : Json(HttpStatusCode.OK, EmptyPage));
        await WaitForIdleAsync(vm);

        vm.Sku = "con espacio";
        vm.Name = "Artículo";
        vm.FormUnit = new UnitItem(Guid.NewGuid(), "UND", "Unidad", true);
        vm.FormVat = VatOption.All[0];
        vm.PriceText = "1";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("El SKU solo admite letras, números, punto, guion y guion bajo.", vm.SkuError);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Iva_es_obligatorio_antes_de_enviar()
    {
        var requests = new List<HttpRequestMessage>();
        var vm = CreateViewModel(requests, _ => Json(HttpStatusCode.OK, EmptyPage));
        await WaitForIdleAsync(vm);
        requests.Clear();

        vm.Sku = "X1";
        vm.Name = "Artículo";
        vm.FormUnit = new UnitItem(Guid.NewGuid(), "UND", "Unidad", true);
        vm.PriceText = "1";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.VatError);
        Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Servicio_no_controla_inventario_y_limpia_el_stock_minimo()
    {
        var vm = CreateViewModel([], _ => Json(HttpStatusCode.OK, EmptyPage));
        await WaitForIdleAsync(vm);

        vm.MinStockText = "5";
        vm.FormKind = KindOption.FormKinds.Single(k => k.Value == "Service");

        Assert.False(vm.IsProduct);
        Assert.False(vm.TracksInventory);
        Assert.Null(vm.MinStockText);

        vm.FormKind = KindOption.FormKinds.Single(k => k.Value == "Product");
        Assert.True(vm.TracksInventory);
    }

    [Theory]
    [InlineData("5", "3,20", "36")]
    [InlineData("0", "1", "")]
    [InlineData("5", "", "")]
    public async Task Margen_se_calcula_sobre_el_precio(string price, string cost, string expectedFragment)
    {
        var vm = CreateViewModel([], _ => Json(HttpStatusCode.OK, EmptyPage));
        await WaitForIdleAsync(vm);

        vm.PriceText = price;
        vm.CostText = cost;

        if (expectedFragment.Length == 0)
        {
            Assert.Equal(string.Empty, vm.MarginText);
        }
        else
        {
            Assert.Contains(expectedFragment, vm.MarginText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Formulario_envia_la_ficha_completa()
    {
        string? body = null;
        var categoryId = Guid.NewGuid();
        var vm = CreateViewModel([], req =>
        {
            if (req.Method != HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, EmptyPage);
            }

            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.BadRequest, """{"status":400,"title":"Datos inválidos","errors":{"Barcode":["Repetido"]}}""");
        });
        await WaitForIdleAsync(vm);

        vm.Sku = "CUAD-002";
        vm.Name = "Cuaderno";
        vm.FormUnit = new UnitItem(Guid.NewGuid(), "UND", "Unidad", true);
        vm.FormCategory = new CategoryOption(categoryId, "Papelería");
        vm.FormVat = VatOption.All.Single(v => v.Value == "ZeroRated");
        vm.PriceText = "5,25";
        vm.CostText = "3,2";
        vm.Barcode = "786123";
        vm.MinStockText = "10";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(body);
        Assert.Contains("\"vatTreatment\":\"ZeroRated\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"categoryId\":\"{categoryId}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"cost\":3.2", body, StringComparison.Ordinal);
        Assert.Contains("\"tracksInventory\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"minStock\":10", body, StringComparison.Ordinal);
        Assert.Equal("Repetido", vm.BarcodeError);
    }

    [Fact]
    public void Consulta_incluye_la_categoria()
    {
        var id = Guid.Parse("0195a000-0000-7000-8000-000000000001");

        Assert.EndsWith("&categoryId=0195a000-0000-7000-8000-000000000001", InventoryApi.Query(1, 20, null, null, null, id), StringComparison.Ordinal);
    }

    private static ProductsViewModel CreateViewModel(List<HttpRequestMessage> requests, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var session = new SessionState();
        session.SignIn("ana.admin@demo.local");
        session.SelectTenant(Guid.NewGuid(), "Demo");
        session.SetTenantDetails(["inventory.products.read", "inventory.products.write", "inventory.categories.read"], ["inventory"]);

        var handler = new SessionHeadersHandler(session)
        {
            InnerHandler = new StubHandler(req =>
            {
                requests.Add(req);
                return responder(req);
            }),
        };
        var api = new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
        return new ProductsViewModel(new InventoryApi(api), session);
    }

    private static async Task WaitForIdleAsync(ProductsViewModel vm)
    {
        for (var i = 0; i < 50 && vm.IsBusy; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
