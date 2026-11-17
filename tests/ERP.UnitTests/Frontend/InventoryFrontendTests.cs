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
        vm.PriceText = "1";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("El SKU solo admite letras, números, punto, guion y guion bajo.", vm.SkuError);
        Assert.NotNull(vm.ErrorMessage);
    }

    private static ProductsViewModel CreateViewModel(List<HttpRequestMessage> requests, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var session = new SessionState();
        session.SignIn("ana.admin@demo.local");
        session.SelectTenant(Guid.NewGuid(), "Demo");
        session.SetTenantDetails(["inventory.products.read", "inventory.products.write"], ["inventory"]);

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
