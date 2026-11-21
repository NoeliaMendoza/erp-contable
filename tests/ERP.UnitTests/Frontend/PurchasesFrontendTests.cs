using System.Net;
using System.Text;
using ERP.Frontend.Modules.Purchases;
using ERP.Frontend.Modules.Purchases.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class PurchasesFrontendTests
{
    private const string Page = """{"items":[],"totalCount":0,"page":1,"pageSize":20}""";
    private const string IdentificationTypes = """[{"sriCode":"04","name":"RUC"},{"sriCode":"05","name":"Cédula"},{"sriCode":"06","name":"Pasaporte"},{"sriCode":"08","name":"Identificación del exterior"}]""";
    private const string Created = """{"id":"0195a000-0000-7000-8000-000000000001","identificationType":"06","identificationNumber":"AB123","name":"Proveedor","email":null,"phone":null,"address":null,"isActive":true}""";

    [Fact]
    public async Task Busca_por_API_y_esconde_edicion_sin_permiso_de_escritura()
    {
        var requests = new List<HttpRequestMessage>();
        var vm = CreateViewModel(["purchases.suppliers.read"], requests, request =>
            Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("identification-types", StringComparison.Ordinal)
                ? IdentificationTypes
                : Page));
        await WaitForIdleAsync(vm);

        vm.Search = "ruc 123";
        await vm.SearchNowCommand.ExecuteAsync(null);

        Assert.False(vm.CanWrite);
        Assert.Contains(requests, request => request.RequestUri!.PathAndQuery.Contains("search=ruc%20123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Guarda_solo_datos_editables_sin_enviar_tenant_ni_auditoria()
    {
        string? body = null;
        var requests = new List<HttpRequestMessage>();
        var vm = CreateViewModel(["purchases.suppliers.read", "purchases.suppliers.write"], requests, request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, Created);
            }

            return Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("identification-types", StringComparison.Ordinal)
                ? IdentificationTypes
                : Page);
        });
        await WaitForIdleAsync(vm);
        requests.Clear();

        vm.SelectedIdentificationType = vm.IdentificationTypes.Single(option => option.SriCode == "06");
        vm.IdentificationNumber = "1790012345001";
        vm.Name = "Proveedor";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(body);
        Assert.Contains("\"identificationType\":\"06\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAtUtc", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isActive", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/suppliers");
    }

    private static SuppliersViewModel CreateViewModel(
        IReadOnlyList<string> permissions,
        List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var session = new SessionState();
        session.SignIn("user@example.test");
        session.SelectTenant(Guid.NewGuid(), "Tenant");
        session.SetTenantDetails(permissions, ["purchases"]);
        var handler = new SessionHeadersHandler(session)
        {
            InnerHandler = new StubHandler(request =>
            {
                requests.Add(request);
                return responder(request);
            })
        };
        var api = new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
        return new SuppliersViewModel(new PurchasesApi(api), session);
    }

    private static async Task WaitForIdleAsync(SuppliersViewModel vm)
    {
        for (var i = 0; i < 100 && vm.IsBusy; i++)
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
