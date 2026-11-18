using System.Net;
using System.Text;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class ErpApiClientTests
{
    [Fact]
    public async Task Envia_token_Bearer_y_empresa_seleccionada_sin_cabecera_de_desarrollo()
    {
        var session = new SessionState();
        var tenant = Guid.NewGuid();
        session.SelectTenant(tenant, "Demo");
        var tokens = new FakeTokens("token-a");
        HttpRequestMessage? captured = null;

        var client = CreateClient(session, req =>
        {
            captured = req;
            return Json(HttpStatusCode.OK, "[]");
        }, tokens);

        await client.GetBranchesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("token-a", captured.Headers.Authorization.Parameter);
        Assert.Equal(tenant.ToString(), captured.Headers.GetValues(SessionHeadersHandler.TenantHeader).Single());
        Assert.False(captured.Headers.Contains("X-Dev-User"));
    }

    [Fact]
    public async Task Sin_sesion_no_envia_Authorization()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(new SessionState(), req =>
        {
            captured = req;
            return Json(HttpStatusCode.OK, "[]");
        }, new FakeTokens(null));

        await client.GetBranchesAsync(TestContext.Current.CancellationToken);

        Assert.Null(captured!.Headers.Authorization);
    }

    [Fact]
    public async Task Respuesta_401_avisa_una_vez_con_el_token_usado_y_no_reintenta()
    {
        var tokens = new FakeTokens("token-a");
        var calls = 0;
        var client = CreateClient(new SessionState(), _ =>
        {
            calls++;
            return Json(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""");
        }, tokens);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetMyProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        Assert.Equal(1, calls);
        Assert.Equal(["token-a"], tokens.Rejected);
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

    private static ErpApiClient CreateClient(
        SessionState session, Func<HttpRequestMessage, HttpResponseMessage> responder, IAccessTokenProvider? tokens = null)
    {
        var handler = new SessionHeadersHandler(session, tokens) { InnerHandler = new StubHandler(responder) };
        return new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeTokens(string? token) : IAccessTokenProvider
    {
        public List<string> Rejected { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);

        public void ReportUnauthorized(string rejectedAccessToken) => Rejected.Add(rejectedAccessToken);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
