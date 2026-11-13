using System.Net;
using System.Text;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class ErpApiClientTests
{
    [Fact]
    public async Task Envia_usuario_y_empresa_seleccionada_en_cabeceras()
    {
        var session = new SessionState();
        session.SignIn("ana.admin@demo.local");
        var tenant = Guid.NewGuid();
        session.SelectTenant(tenant, "Demo");
        HttpRequestMessage? captured = null;

        var client = CreateClient(session, req =>
        {
            captured = req;
            return Json(HttpStatusCode.OK, "[]");
        });

        await client.GetBranchesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ana.admin@demo.local", captured!.Headers.GetValues(SessionHeadersHandler.DevUserHeader).Single());
        Assert.Equal(tenant.ToString(), captured.Headers.GetValues(SessionHeadersHandler.TenantHeader).Single());
    }

    [Fact]
    public async Task Traduce_ProblemDetails_de_validacion_a_mensaje_legible()
    {
        var client = CreateClient(new SessionState(), _ => Json(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"Code":["El código solo admite letras, números y guiones."]}}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            client.CreateBranchAsync(new CreateBranch("x y", "n", null, null), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        Assert.Contains("letras, números y guiones", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prohibido_muestra_el_detalle_del_servidor()
    {
        var client = CreateClient(new SessionState(), _ => Json(HttpStatusCode.Forbidden,
            """{"title":"Acceso denegado","status":403,"detail":"No tiene una membresía activa en la empresa seleccionada."}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetCurrentTenantAsync(TestContext.Current.CancellationToken));

        Assert.Equal("No tiene una membresía activa en la empresa seleccionada.", ex.Message);
    }

    private static ErpApiClient CreateClient(SessionState session, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new SessionHeadersHandler(session) { InnerHandler = new StubHandler(responder) };
        return new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
