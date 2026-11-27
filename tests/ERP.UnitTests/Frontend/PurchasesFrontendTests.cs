using System.Net;
using System.Text;
using System.Text.Json;
using ERP.Frontend.Modules.Inventory;
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
    public async Task Registrar_carga_catalogos_y_envia_las_lineas_del_borrador()
    {
        var requests = new List<HttpRequestMessage>();
        var draft = DraftPurchase();
        var product = new ProductItem(draft.Lines[0].ProductId, "CUAD", "Cuaderno", "Product",
            Guid.NewGuid(), "UND", "Unidad", 5m, true, "ZeroRated", null, null, null, null, null, true, null);
        var supplier = new SupplierItem(draft.SupplierId, "06", "AB123", "Proveedor", null, null, null, true);
        var api = CreateApi(["purchases.documents.register"], requests, request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Created, JsonSerializer.Serialize(draft));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/suppliers" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = new[] { supplier } })),
                "/api/v1/products" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = new[] { product } })),
                _ => Json(HttpStatusCode.OK, Page),
            };
        }, out var session);
        var vm = new PurchasesViewModel(new PurchasesApi(api), new InventoryApi(api), session);
        await WaitForIdleAsync(vm);

        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        vm.SelectedSupplier = Assert.Single(vm.Suppliers);
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        vm.AddLineCommand.Execute(null);
        Assert.NotNull(vm.ErrorMessage);
        vm.SelectedProduct = Assert.Single(vm.Products);
        vm.QuantityText = "0";
        vm.AddLineCommand.Execute(null);
        Assert.Empty(vm.Lines);
        vm.QuantityText = "10";
        vm.UnitCostText = "-3";
        vm.AddLineCommand.Execute(null);
        Assert.Empty(vm.Lines);
        vm.UnitCostText = "3";
        vm.AddLineCommand.Execute(null);
        vm.AddLineCommand.Execute(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(30m, vm.DraftTotal);
        vm.RemoveLineCommand.Execute(Assert.Single(vm.Lines));
        Assert.Equal(0m, vm.DraftTotal);
        vm.AddLineCommand.Execute(null);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);

        await vm.RegisterCommand.ExecuteAsync(null);

        var post = Assert.Single(requests, request => request.Method == HttpMethod.Post);
        Assert.Equal("/api/v1/purchases", post.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(await post.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(supplier.Id, body.RootElement.GetProperty("supplierId").GetGuid());
        var line = Assert.Single(body.RootElement.GetProperty("lines").EnumerateArray());
        Assert.Equal(product.Id, line.GetProperty("productId").GetGuid());
        Assert.Equal(10m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(3m, line.GetProperty("unitCost").GetDecimal());
        Assert.Equal(draft.Id, Assert.Single(vm.Purchases).Id);
        Assert.Empty(vm.Lines);
        Assert.Null(vm.SelectedSupplier);
    }

    [Fact]
    public async Task Buscar_y_paginar_consulta_la_API_con_la_pagina_y_el_texto_correctos()
    {
        var requests = new List<HttpRequestMessage>();
        var api = CreateApi(["purchases.documents.read"], requests, _ =>
            Json(HttpStatusCode.OK, """{"items":[],"totalCount":21,"page":1,"pageSize":20,"totalPages":2}"""), out var session);
        var vm = new PurchasesViewModel(new PurchasesApi(api), new InventoryApi(api), session);
        await WaitForIdleAsync(vm);

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.True(vm.CanGoPrevious);
        Assert.False(vm.CanGoNext);
        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.False(vm.CanGoPrevious);
        vm.Search = "proveedor uno";
        await vm.SearchNowCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Page);
        Assert.Contains(requests, request => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal));
        Assert.Contains("search=proveedor%20uno", requests[^1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmar_recepcion_envia_la_empresa_activa_y_actualiza_el_borrador()
    {
        var requests = new List<HttpRequestMessage>();
        var draft = DraftPurchase();
        var confirmed = draft with { Status = "Confirmed", StockMovementId = Guid.NewGuid(), JournalEntryId = Guid.NewGuid() };
        var api = CreateApi(["purchases.documents.read", "purchases.documents.register"], requests, request =>
            request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(confirmed))
                : PurchasePage(request, draft), out var session);
        var vm = new PurchasesViewModel(new PurchasesApi(api), new InventoryApi(api), session);
        await WaitForIdleAsync(vm);
        var purchase = Assert.Single(vm.Purchases);

        vm.AskConfirmationCommand.Execute(purchase);
        vm.CancelConfirmationCommand.Execute(null);
        Assert.Null(vm.PendingConfirmation);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
        vm.AskConfirmationCommand.Execute(purchase);
        await vm.ConfirmCommand.ExecuteAsync(null);

        var post = Assert.Single(requests, request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/purchases/{draft.Id:D}/confirm", post.RequestUri!.AbsolutePath);
        Assert.Equal(session.TenantId.ToString(), Assert.Single(post.Headers.GetValues(SessionHeadersHandler.TenantHeader)));
        var updated = Assert.Single(vm.Purchases);
        Assert.False(updated.IsDraft);
        Assert.Equal("Confirmada", updated.StatusLabel);
        Assert.Equal(30m, updated.TotalCost);
        Assert.Contains("30", vm.SuccessMessage);
        vm.AskConfirmationCommand.Execute(updated);
        Assert.Null(vm.PendingConfirmation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conserva_el_borrador_si_la_confirmacion_falla_o_el_usuario_solo_consulta(bool canRegister)
    {
        var requests = new List<HttpRequestMessage>();
        var draft = DraftPurchase();
        string[] permissions = canRegister
            ? ["purchases.documents.read", "purchases.documents.register"]
            : ["purchases.documents.read"];
        var api = CreateApi(permissions, requests, request =>
            request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.UnprocessableEntity, """{"detail":"Cuenta de Caja no disponible."}""")
                : PurchasePage(request, draft), out var session);
        var vm = new PurchasesViewModel(new PurchasesApi(api), new InventoryApi(api), session);
        await WaitForIdleAsync(vm);

        vm.AskConfirmationCommand.Execute(Assert.Single(vm.Purchases));
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.Purchases).IsDraft);
        Assert.Null(vm.SuccessMessage);
        Assert.Equal(canRegister ? "Cuenta de Caja no disponible." : null, vm.ErrorMessage);
        Assert.Equal(canRegister ? 1 : 0, requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static PurchaseItem DraftPurchase() =>
        new(Guid.NewGuid(), "C-001", Guid.NewGuid(), "Proveedor", new DateOnly(2026, 11, 26),
            Guid.NewGuid(), "Draft", 30m, null, null,
            [new PurchaseLineItem(Guid.NewGuid(), Guid.NewGuid(), "CUAD", "Cuaderno", "UND", 10m, 3m, 30m)]);

    private static HttpResponseMessage PurchasePage(HttpRequestMessage request, PurchaseItem draft) =>
        Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath == "/api/v1/purchases"
            ? JsonSerializer.Serialize(new { items = new[] { draft }, totalCount = 1, page = 1, pageSize = 20 })
            : Page);

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
        var api = CreateApi(permissions, requests, responder, out var session);
        return new SuppliersViewModel(new PurchasesApi(api), session);
    }

    private static ErpApiClient CreateApi(
        IReadOnlyList<string> permissions,
        List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        out SessionState session)
    {
        session = new SessionState();
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
        return new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });
    }

    private static async Task WaitForIdleAsync(ERP.Frontend.Shared.Mvvm.ViewModelBase vm)
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
